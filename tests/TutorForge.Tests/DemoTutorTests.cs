using System.Text;
using TutorForge.Core.Models;
using TutorForge.Infrastructure.Demo;

namespace TutorForge.Tests;

public class DemoTutorTests
{
    [Fact]
    public async Task Streams_a_reply_in_several_chunks()
    {
        var chunks = new List<string>();
        await foreach (var c in new DemoTutor().StreamReplyAsync(TestData.Request()))
        {
            chunks.Add(c);
        }

        Assert.True(chunks.Count > 10);
        Assert.Contains("step by step", string.Concat(chunks), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TutorLanguage.Spanish, "paso a paso")]
    [InlineData(TutorLanguage.French, "étape par étape")]
    [InlineData(TutorLanguage.Catalan, "pas a pas")]
    public void Answers_in_the_requested_language(TutorLanguage language, string expected)
    {
        Assert.Contains(expected, DemoTutor.BuildReply(TestData.Request(language: language)), StringComparison.Ordinal);
    }

    [Fact]
    public void Practice_mode_proposes_an_exercise()
    {
        Assert.Contains("Exercise 1", DemoTutor.BuildReply(TestData.Request(mode: TutorMode.Practice)), StringComparison.Ordinal);
    }

    [Fact]
    public void Guide_mode_gives_a_hint_on_the_second_turn()
    {
        var request = TestData.Request() with
        {
            Messages =
            [
                new ChatMessage(ChatRole.User, "2x + 3 = 11"),
                new ChatMessage(ChatRole.Assistant, "What do you know?"),
                new ChatMessage(ChatRole.User, "I need x"),
            ],
        };
        Assert.Contains("Hint", DemoTutor.BuildReply(request), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejects_invalid_requests()
    {
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await foreach (var _ in new DemoTutor().StreamReplyAsync(new TutorRequest([])))
            {
            }
        });
    }

    /// <summary>Builds a conversation with <paramref name="userTurns"/> student messages, replaying the demo's own answers.</summary>
    private static TutorRequest Conversation(int userTurns, Subject subject = Subject.Mathematics,
        TutorMode mode = TutorMode.Assess, TutorLanguage language = TutorLanguage.English, int seed = 42)
    {
        var request = TestData.Request(mode: mode, language: language) with { Subject = subject, Messages = [] };
        var messages = new List<ChatMessage>();
        for (var i = 1; i <= userTurns; i++)
        {
            messages.Add(new ChatMessage(ChatRole.User, $"answer {i}"));
            if (i < userTurns)
            {
                messages.Add(new ChatMessage(ChatRole.Assistant, DemoTutor.BuildReply(request with { Messages = [.. messages] }, seed)));
            }
        }

        return request with { Messages = messages };
    }

    private static string QuestionLine(string reply) =>
        reply.Split("\n\n").First(l => l.StartsWith("**Question", StringComparison.Ordinal) || l.StartsWith("**Exercise", StringComparison.Ordinal));

    [Fact]
    public void Assess_mode_starts_by_itself_on_the_chosen_subject_with_a_hidden_seed()
    {
        var reply = DemoTutor.BuildReply(Conversation(1, Subject.Biology), 42);

        Assert.Contains("Biology", reply, StringComparison.Ordinal);
        Assert.Contains("Question 1/5", reply, StringComparison.Ordinal);
        Assert.Contains("[[SEED:42]]", reply, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Subject.HistoryGeography, TutorLanguage.Spanish, "Pregunta 1/5")]
    [InlineData(Subject.Programming, TutorLanguage.French, "Question 1/5")]
    [InlineData(Subject.English, TutorLanguage.Catalan, "Pregunta 1/5")]
    public void Questions_come_from_the_subject_bank_in_the_right_language(Subject subject, TutorLanguage language, string label)
    {
        var reply = DemoTutor.BuildReply(Conversation(1, subject, language: language), 7);
        Assert.Contains(label, reply, StringComparison.Ordinal);
        Assert.DoesNotContain("3x - 4", reply, StringComparison.Ordinal); // not the maths script
    }

    [Fact]
    public void The_five_questions_of_a_test_are_all_different()
    {
        var questions = Enumerable.Range(1, 5)
            .Select(turn => QuestionLine(DemoTutor.BuildReply(Conversation(turn, Subject.Biology), 99)).Split(' ', 3)[2])
            .ToList();

        Assert.Equal(5, questions.Distinct().Count());
    }

    [Fact]
    public void Different_sessions_get_a_different_order()
    {
        var firstQuestions = Enumerable.Range(1, 20)
            .Select(seed => QuestionLine(DemoTutor.BuildReply(Conversation(1, Subject.Mathematics, seed: seed), seed)))
            .Distinct()
            .Count();

        Assert.True(firstQuestions > 3);
    }

    [Fact]
    public void The_seed_is_reused_and_not_repeated_after_the_first_answer()
    {
        var reply = DemoTutor.BuildReply(Conversation(2, Subject.Biology), 1234);
        Assert.DoesNotContain("[[SEED:", reply, StringComparison.Ordinal);
    }

    [Fact]
    public void Assess_mode_gives_a_level_report_then_the_level_grows()
    {
        var report = DemoTutor.BuildReply(Conversation(6), 42);
        var next = DemoTutor.BuildReply(Conversation(7), 42);

        var level = 4 + (42 % 4);
        Assert.Contains($"[[LEVEL:{level}]]", report, StringComparison.Ordinal);
        Assert.Contains($"[[LEVEL:{level + 1}]]", next, StringComparison.Ordinal);
    }

    [Fact]
    public void Practice_mode_uses_exercises_of_the_subject()
    {
        var reply = DemoTutor.BuildReply(Conversation(1, Subject.PhysicsChemistry, TutorMode.Practice), 5);
        Assert.Contains("Physics & chemistry", reply, StringComparison.Ordinal);
        Assert.Contains("Exercise 1", reply, StringComparison.Ordinal);
    }

    [Fact]
    public void Shuffle_is_a_permutation()
    {
        Assert.Equal(Enumerable.Range(0, 7), DemoTutor.Shuffle(7, 3).Order());
    }
}
