using TutorForge.Core.Models;
using TutorForge.Core.Progress;

namespace TutorForge.Tests;

public class ProgressServiceTests
{
    private static readonly Guid Student = Guid.NewGuid();
    private readonly ManualTimeProvider _clock = new(new DateTimeOffset(2026, 11, 13, 10, 0, 0, TimeSpan.Zero));
    private readonly InMemoryStudentProgressStore _store;
    private readonly ProgressService _service;

    public ProgressServiceTests()
    {
        _store = new InMemoryStudentProgressStore(_clock);
        _service = new ProgressService(_store, _clock);
    }

    private static SaveProgressRequest Request(int? level = null, string? topic = "Fractions", int messages = 2) =>
        new(topic, Level.Secondary, TutorMode.Assess, TutorLanguage.Spanish, level,
            Enumerable.Range(0, messages).Select(i => new ChatMessage(i % 2 == 0 ? ChatRole.User : ChatRole.Assistant, $"m{i}")).ToList());

    [Fact]
    public async Task Saves_and_reads_the_progress_of_a_subject()
    {
        Assert.Empty(await _service.SaveAsync(Student, Subject.Mathematics, Request(level: 5)));

        var progress = await _service.GetAsync(Student);
        Assert.NotNull(progress);
        Assert.Equal(Subject.Mathematics, progress.LastSubject);
        var maths = progress.Subjects[Subject.Mathematics];
        Assert.Equal("Fractions", maths.Topic);
        Assert.Equal(5, maths.EstimatedLevel);
        Assert.Equal(2, maths.Messages.Count);
    }

    [Fact]
    public async Task Level_history_only_grows_when_the_level_changes()
    {
        await _service.SaveAsync(Student, Subject.Mathematics, Request(level: 5));
        _clock.Advance(TimeSpan.FromMinutes(5));
        await _service.SaveAsync(Student, Subject.Mathematics, Request(level: 5));
        _clock.Advance(TimeSpan.FromMinutes(5));
        await _service.SaveAsync(Student, Subject.Mathematics, Request(level: 7));

        var history = (await _service.GetAsync(Student))!.Subjects[Subject.Mathematics].LevelHistory;
        Assert.Equal(new[] { 5, 7 }, history.Select(p => p.Level));
        Assert.Equal(_clock.Now, history[^1].At);
    }

    [Fact]
    public async Task Keeps_the_last_level_when_a_save_has_no_level()
    {
        await _service.SaveAsync(Student, Subject.Mathematics, Request(level: 6));
        await _service.SaveAsync(Student, Subject.Mathematics, Request(level: null));

        Assert.Equal(6, (await _service.GetAsync(Student))!.Subjects[Subject.Mathematics].EstimatedLevel);
    }

    [Fact]
    public async Task Each_subject_is_saved_separately_and_last_subject_is_tracked()
    {
        await _service.SaveAsync(Student, Subject.Mathematics, Request(level: 4));
        await _service.SaveAsync(Student, Subject.English, Request(level: 8, topic: "Past simple"));

        var progress = (await _service.GetAsync(Student))!;
        Assert.Equal(Subject.English, progress.LastSubject);
        Assert.Equal(4, progress.Subjects[Subject.Mathematics].EstimatedLevel);
        Assert.Equal("Past simple", progress.Subjects[Subject.English].Topic);
    }

    [Theory]
    [InlineData(11)]
    [InlineData(-1)]
    public async Task Rejects_invalid_levels(int level)
    {
        Assert.NotEmpty(await _service.SaveAsync(Student, Subject.Mathematics, Request(level: level)));
        Assert.Null(await _service.GetAsync(Student));
    }

    [Fact]
    public async Task Rejects_too_long_conversations()
    {
        var errors = await _service.SaveAsync(Student, Subject.Mathematics, Request(messages: TutorRequest.MaxMessages + 2));
        Assert.Contains(errors, e => e.Code == "ConversationTooLong");
    }

    [Fact]
    public async Task Delete_forgets_the_student()
    {
        await _service.SaveAsync(Student, Subject.Mathematics, Request(level: 5));
        await _service.DeleteAsync(Student);
        Assert.Null(await _service.GetAsync(Student));
    }
}
