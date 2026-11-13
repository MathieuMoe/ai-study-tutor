using System.Globalization;
using TutorForge.Core.Models;

namespace TutorForge.Web;

public static class Localization
{
    /// <summary>Interface languages, with their name written in that language (for the switcher).</summary>
    public static readonly IReadOnlyList<(string Culture, string Name)> Cultures =
    [
        ("en", "English"),
        ("fr", "Français"),
        ("es", "Español"),
        ("ca", "Català"),
    ];

    public static readonly string[] SupportedCultures = [.. Cultures.Select(c => c.Culture)];

    public static string CurrentCulture => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

    /// <summary>Default tutor language = interface language.</summary>
    public static TutorLanguage TutorLanguageForCurrentCulture() => CurrentCulture switch
    {
        "fr" => TutorLanguage.French,
        "es" => TutorLanguage.Spanish,
        "ca" => TutorLanguage.Catalan,
        _ => TutorLanguage.English,
    };

    /// <summary>Only redirect to local paths (prevents open redirects).</summary>
    public static bool IsLocalUrl(string? url) =>
        !string.IsNullOrEmpty(url) && url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));
}
