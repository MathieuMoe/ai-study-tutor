using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.WebEncoders;
using TutorForge.Core.Abstractions;
using TutorForge.Core.Models;
using TutorForge.Core.Validation;
using TutorForge.Infrastructure;
using TutorForge.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLocalization(o => o.ResourcesPath = "Resources");
builder.Services.Configure<RequestLocalizationOptions>(o => o
    .SetDefaultCulture("en")
    .AddSupportedCultures(Localization.SupportedCultures)
    .AddSupportedUICultures(Localization.SupportedCultures));
builder.Services.AddRazorPages();
// Write accented characters (é, ñ, ç, à…) as-is in the HTML instead of &#xE9; entities.
builder.Services.Configure<WebEncoderOptions>(o => o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));
builder.Services.AddTutorForge(builder.Configuration);
// Progress files live under the app's content root (App_Data is excluded from git).
builder.Services.PostConfigure<TutorForge.Infrastructure.Progress.ProgressStoreOptions>(o =>
    o.Directory = Path.Combine(builder.Environment.ContentRootPath, o.Directory));
builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Protects the (paid) AI API from abuse: 20 messages per minute per client IP.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("chat", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
    options.AddPolicy("progress", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRequestLocalization();
app.UseRouting();
app.UseRateLimiter();

app.MapRazorPages();
app.MapHealthChecks("/health");

// Interface language switcher: stores the choice in the standard ASP.NET Core culture cookie.
app.MapGet("/culture/{culture}", (string culture, string? returnUrl, HttpContext context) =>
{
    if (!Localization.SupportedCultures.Contains(culture))
    {
        culture = "en";
    }

    context.Response.Cookies.Append(
        CookieRequestCultureProvider.DefaultCookieName,
        CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
        new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, SameSite = SameSiteMode.Lax });

    return Results.LocalRedirect(Localization.IsLocalUrl(returnUrl) ? returnUrl! : "/");
});

// Chat endpoint. Stateless: the browser sends the whole conversation, the server stores nothing.
// The answer is streamed as server-sent events: data: {"text":"..."} ... event: done.
app.MapPost("/api/tutor/stream", async (TutorRequest request, ITutor tutor, HttpContext context, ILogger<Program> logger) =>
{
    var errors = TutorRequestValidator.Check(request);
    if (errors.Count > 0)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { code = errors[0].Code, detail = errors[0].Message });
        return;
    }

    var ct = context.RequestAborted;
    context.Response.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers["X-Accel-Buffering"] = "no"; // disable proxy buffering (nginx)

    try
    {
        await foreach (var chunk in tutor.StreamReplyAsync(request, ct))
        {
            await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(new { text = chunk })}\n\n", ct);
            await context.Response.Body.FlushAsync(ct);
        }

        await context.Response.WriteAsync("event: done\ndata: {}\n\n", ct);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        // The student pressed "Stop" or closed the page: nothing to do.
    }
    catch (Exception ex) when (ex is TutorException or HttpRequestException or TaskCanceledException)
    {
        Log.TutorFailed(logger, ex);
        await context.Response.WriteAsync("event: error\ndata: {\"code\":\"ai_error\"}\n\n", CancellationToken.None);
    }
})
.RequireRateLimiting("chat");

app.MapProgressEndpoints();

await app.RunAsync();

/// <summary>Exposed for integration tests (WebApplicationFactory).</summary>
public partial class Program;

internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Tutor answer failed")]
    public static partial void TutorFailed(ILogger logger, Exception ex);
}
