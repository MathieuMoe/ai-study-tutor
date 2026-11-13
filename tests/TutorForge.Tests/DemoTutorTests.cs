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
}
