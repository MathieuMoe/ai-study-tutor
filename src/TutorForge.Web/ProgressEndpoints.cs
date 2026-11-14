using TutorForge.Core.Models;
using TutorForge.Core.Progress;
using TutorForge.Core.Records;

namespace TutorForge.Web;

/// <summary>
/// Anonymous student identity: a random id in an HttpOnly cookie. No account and no personal data.
/// With real authentication this would simply become the user id.
/// </summary>
public static class StudentIdentity
{
    public const string CookieName = "tf_student";
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(90);

    public static Guid? Get(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Request.Cookies.TryGetValue(CookieName, out var value) && Guid.TryParseExact(value, "N", out var id)
            ? id
            : null;
    }

    public static Guid GetOrCreate(HttpContext context)
    {
        var id = Get(context) ?? Guid.NewGuid();
        // Sliding expiration: the cookie is renewed each time progress is saved.
        context.Response.Cookies.Append(CookieName, id.ToString("N"), new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            Expires = DateTimeOffset.UtcNow.Add(Lifetime),
        });
        return id;
    }

    public static void Forget(HttpContext context) => context?.Response.Cookies.Delete(CookieName);
}

public static class ProgressEndpoints
{
    public static IEndpointRouteBuilder MapProgressEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/progress").RequireRateLimiting("progress");

        // Everything the student has done, per subject (null when nothing was saved yet).
        group.MapGet("/", async (HttpContext context, ProgressService progress, CancellationToken ct) =>
            StudentIdentity.Get(context) is { } id && await progress.GetAsync(id, ct) is { } data
                ? Results.Ok(data)
                : Results.NoContent());

        // Saves where the student is in one subject (called by the browser after each answer).
        group.MapPut("/{subject}", async (Subject subject, SaveProgressRequest body, HttpContext context, ProgressService progress, CancellationToken ct) =>
        {
            var id = StudentIdentity.GetOrCreate(context);
            var errors = await progress.SaveAsync(id, subject, body, ct);
            return errors.Count == 0
                ? Results.NoContent()
                : Results.BadRequest(new { code = errors[0].Code, detail = errors[0].Message });
        });

        // "Forget me": deletes the student's progress, record and the cookie.
        group.MapDelete("/", async (HttpContext context, ProgressService progress, IStudentRecordStore records, CancellationToken ct) =>
        {
            if (StudentIdentity.Get(context) is { } id)
            {
                await progress.DeleteAsync(id, ct);
                await records.DeleteAsync(id, ct);
            }

            StudentIdentity.Forget(context);
            return Results.NoContent();
        });

        return app;
    }
}
