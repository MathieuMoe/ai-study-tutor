using Microsoft.AspNetCore.Mvc.RazorPages;
using TutorForge.Core.Abstractions;
using TutorForge.Core.Models;

namespace TutorForge.Web.Pages;

/// <summary>The chat page. The conversation itself happens in the browser through /api/tutor/stream.</summary>
public sealed class IndexModel(ITutor tutor) : PageModel
{
    public TutorLanguage DefaultLanguage { get; private set; }

    public void OnGet()
    {
        ViewData["Engine"] = tutor.EngineName;
        DefaultLanguage = Localization.TutorLanguageForCurrentCulture();
    }
}
