using TutorForge.Infrastructure.Engines;

namespace TutorForge.Web;

/// <summary>Remembers the AI engine chosen in the interface (cookie), like the language switcher.</summary>
public static class EngineSelection
{
    public const string CookieName = "ai_engine";

    public static TutorEngine Current(HttpContext context, TutorEngines engines)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(engines);
        return engines.Get(context.Request.Cookies[CookieName]);
    }

    public static IEndpointRouteBuilder MapEngineSelection(this IEndpointRouteBuilder app)
    {
        app.MapGet("/engine/{id}", (string id, string? returnUrl, HttpContext context, TutorEngines engines) =>
        {
            var engine = engines.Get(id);
            context.Response.Cookies.Append(CookieName, engine.Id, new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                HttpOnly = true,
            });
            return Results.LocalRedirect(Localization.IsLocalUrl(returnUrl) ? returnUrl! : "/");
        });
        return app;
    }
}
