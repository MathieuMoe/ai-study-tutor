using Microsoft.Extensions.Options;
using TutorForge.Core.Models;
using TutorForge.Core.Progress;
using TutorForge.Infrastructure.Progress;

namespace TutorForge.Tests;

public sealed class FileStudentProgressStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tutorforge-tests-" + Guid.NewGuid().ToString("N"));
    private readonly ManualTimeProvider _clock = new(DateTimeOffset.UtcNow);
    private readonly FileStudentProgressStore _store;

    public FileStudentProgressStoreTests() =>
        _store = new FileStudentProgressStore(Options.Create(new ProgressStoreOptions { Directory = _dir }), _clock);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private SubjectProgress Progress(Subject subject, int level) => new(
        subject, "Topic", Level.HighSchool, TutorMode.Practice, TutorLanguage.French, level,
        [new LevelPoint(level, _clock.GetUtcNow())],
        [new ChatMessage(ChatRole.User, "Bonjour"), new ChatMessage(ChatRole.Assistant, "Salut ! $x^2$ [[LEVEL:3]]")],
        _clock.GetUtcNow());

    [Fact]
    public async Task Round_trips_a_student_through_a_json_file()
    {
        var id = Guid.NewGuid();
        await _store.SaveSubjectAsync(id, Progress(Subject.Biology, 3));

        var read = await _store.GetAsync(id);

        Assert.NotNull(read);
        Assert.Equal(Subject.Biology, read.LastSubject);
        var biology = read.Subjects[Subject.Biology];
        Assert.Equal(TutorLanguage.French, biology.Language);
        Assert.Equal("Salut ! $x^2$ [[LEVEL:3]]", biology.Messages[1].Content);
        Assert.True(File.Exists(Path.Combine(_dir, id.ToString("N") + ".json")));
    }

    [Fact]
    public async Task Unknown_student_returns_null() => Assert.Null(await _store.GetAsync(Guid.NewGuid()));

    [Fact]
    public async Task Concurrent_saves_of_different_subjects_are_all_kept()
    {
        var id = Guid.NewGuid();
        var subjects = Enum.GetValues<Subject>();
        await Task.WhenAll(subjects.Select(s => _store.SaveSubjectAsync(id, Progress(s, 5))));

        Assert.Equal(subjects.Length, (await _store.GetAsync(id))!.Subjects.Count);
    }

    [Fact]
    public async Task Delete_removes_the_file()
    {
        var id = Guid.NewGuid();
        await _store.SaveSubjectAsync(id, Progress(Subject.English, 6));
        await _store.DeleteAsync(id);

        Assert.Null(await _store.GetAsync(id));
    }

    [Fact]
    public async Task Purge_deletes_inactive_students_only()
    {
        var oldStudent = Guid.NewGuid();
        var activeStudent = Guid.NewGuid();
        await _store.SaveSubjectAsync(oldStudent, Progress(Subject.English, 6));
        await _store.SaveSubjectAsync(activeStudent, Progress(Subject.English, 6));
        File.SetLastWriteTimeUtc(Path.Combine(_dir, oldStudent.ToString("N") + ".json"), DateTime.UtcNow.AddDays(-120));

        var deleted = await _store.PurgeInactiveAsync(TimeSpan.FromDays(90));

        Assert.Equal(1, deleted);
        Assert.Null(await _store.GetAsync(oldStudent));
        Assert.NotNull(await _store.GetAsync(activeStudent));
    }
}
