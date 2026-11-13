using Microsoft.AspNetCore.Mvc.RazorPages;
using TutorForge.Core.Abstractions;

namespace TutorForge.Web.Pages;

public sealed class AboutModel(ITutor tutor) : PageModel
{
    public void OnGet() => ViewData["Engine"] = tutor.EngineName;
}
