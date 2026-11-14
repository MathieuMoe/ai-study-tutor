using System.Text.Json;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TutorForge.Core.Models;
using TutorForge.Core.Progress;
using TutorForge.Core.Records;
using TutorForge.Infrastructure.Records;

namespace TutorForge.Web.Pages;

/// <summary>"My progress": the student record, the tutor's level per subject and the recommended next steps.</summary>
public sealed class ProgressModel(IStudentRecordStore records, ProgressService progress, TimeProvider clock) : PageModel
{
    public StudentRecord? Record { get; private set; }

    public StudentProgress? Progress { get; private set; }

    public IReadOnlyList<Recommendation> Recommendations { get; private set; } = [];

    public DateOnly Today { get; private set; }

    public string RecordJson { get; private set; } = "{}";

    /// <summary>Subjects with a course or with tutor progress, courses first.</summary>
    public IReadOnlyList<Subject> Subjects { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (StudentIdentity.Get(HttpContext) is { } id)
        {
            Record = await records.GetAsync(id, cancellationToken);
            Progress = await progress.GetAsync(id, cancellationToken);
        }

        Recommendations = ProgressionAdvisor.Recommend(Record, Progress, Today);
        Subjects = (Record?.Enrollments.Select(e => e.Subject) ?? [])
            .Concat(Progress?.Subjects.Keys ?? Enumerable.Empty<Subject>())
            .Distinct()
            .ToList();
        RecordJson = JsonSerializer.Serialize(Record ?? StudentRecord.Empty(Guid.Empty, clock.GetUtcNow()), FileStudentRecordStore.Json);
    }

    /// <summary>Link that opens the tutor on a subject, mode and topic and starts right away.</summary>
    public static string TutorLink(Subject subject, TutorMode mode, string? topic) =>
        $"/?subject={subject}&mode={mode}&start=1" + (string.IsNullOrWhiteSpace(topic) ? string.Empty : "&topic=" + Uri.EscapeDataString(topic));
}
