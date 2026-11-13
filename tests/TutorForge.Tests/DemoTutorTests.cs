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

    private static TutorRequest Turn(int userTurns, TutorMode mode = TutorMode.Assess)
    {
        var messages = new List<ChatMessage>();
        for (var i = 1; i <= userTurns; i++)
        {
            messages.Add(new ChatMessage(ChatRole.User, $"answer {i}"));
            if (i < userTurns)
            {
                messages.Add(new ChatMessage(ChatRole.Assistant, "question"));
            }
        }

        return TestData.Request(mode: mode) with { Messages = messages };
    }

    [Fact]
    public void Assess_mode_starts_with_question_1_without_any_question_from_the_student()
    {
        var reply = DemoTutor.BuildReply(Turn(1));
        Assert.Contains("Question 1/5", reply, StringComparison.Ordinal);
        Assert.DoesNotContain("[[LEVEL", reply, StringComparison.Ordinal);
    }

    [Fact]
    public void Assess_mode_gives_a_level_report_then_the_level_grows()
    {
        Assert.Contains("[[LEVEL:6]]", DemoTutor.BuildReply(Turn(6)), StringComparison.Ordinal);
        Assert.Contains("[[LEVEL:7]]", DemoTutor.BuildReply(Turn(7)), StringComparison.Ordinal);
        Assert.Contains("[[LEVEL:8]]", DemoTutor.BuildReply(Turn(8)), StringComparison.Ordinal);
    }
}
