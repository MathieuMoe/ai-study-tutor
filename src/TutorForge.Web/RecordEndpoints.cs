using System.Text.Json;
using TutorForge.Core.Models;
using TutorForge.Core.Progress;
using TutorForge.Core.Records;
using TutorForge.Infrastructure.Records;

namespace TutorForge.Web;

/// <summary>
/// The student record API. In the demo the browser can read, replace (JSON editor), fill with a sample student,
/// export and delete its own record; on a real platform the record would be fed by the platform itself.
/// </summary>
public static class RecordEndpoints
{
    public static IEndpointRouteBuilder MapRecordEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/record").RequireRateLimiting("progress");

        group.MapGet("/", async (HttpContext context, IStudentRecordStore records, CancellationToken ct) =>
            StudentIdentity.Get(context) is { } id && await records.GetAsync(id, ct) is { } record
                ? Results.Ok(record)
                : Results.NoContent());

        // Replace the record (JSON editor or import). Everything is sanitised: the JSON comes from the user.
        group.MapPut("/", async (StudentRecord body, HttpContext context, IStudentRecordStore records, TimeProvider clock, CancellationToken ct) =>
        {
            var id = StudentIdentity.GetOrCreate(context);
            var record = RecordRules.Sanitize(body, id, clock.GetUtcNow());
            await records.SaveAsync(record, ct);
            return Results.Ok(record);
        });

        // Demo: a realistic student (courses, results, sessions, goals, teacher notes)
        group.MapPost("/sample", async (TutorLanguage? language, HttpContext context, IStudentRecordStore records, TimeProvider clock, CancellationToken ct) =>
        {
            var id = StudentIdentity.GetOrCreate(context);
            var record = SampleRecord.Create(id, language ?? Localization.TutorLanguageForCurrentCulture(), clock.GetUtcNow());
            await records.SaveAsync(record, ct);
            return Results.Ok(record);
        });

        // Everything we store about the student, in one JSON file (right of access / portability)
        group.MapGet("/export", async (HttpContext context, IStudentRecordStore records, ProgressService progress, CancellationToken ct) =>
        {
            if (StudentIdentity.Get(context) is not { } id)
            {
                return Results.NoContent();
            }

            var data = new { record = await records.GetAsync(id, ct), progress = await progress.GetAsync(id, ct) };
            return Results.File(JsonSerializer.SerializeToUtf8Bytes(data, FileStudentRecordStore.Json), "application/json", "matgpt-student.json");
        });

        group.MapDelete("/", async (HttpContext context, IStudentRecordStore records, CancellationToken ct) =>
        {
            if (StudentIdentity.Get(context) is { } id)
            {
                await records.DeleteAsync(id, ct);
            }

            return Results.NoContent();
        });

        return app;
    }
}
