using TutorForge.Core.Models;
using TutorForge.Core.Prompts;

namespace TutorForge.Tests;

public class ConversationNormalizerTests
{
    [Fact]
    public void Drops_leading_tutor_messages_and_empty_ones()
    {
        var result = ConversationNormalizer.Normalize(
        [
            new ChatMessage(ChatRole.Assistant, "Welcome!"),
            new ChatMessage(ChatRole.User, "  "),
            new ChatMessage(ChatRole.User, "Hello"),
        ]);

        var only = Assert.Single(result);
        Assert.Equal(ChatRole.User, only.Role);
        Assert.Equal("Hello", only.Content);
    }

    [Fact]
    public void Merges_consecutive_messages_of_the_same_role()
    {
        var result = ConversationNormalizer.Normalize(
        [
            new ChatMessage(ChatRole.User, "Question part 1"),
            new ChatMessage(ChatRole.User, "part 2"),
            new ChatMessage(ChatRole.Assistant, "Answer"),
            new ChatMessage(ChatRole.User, "Thanks"),
        ]);

        Assert.Equal(3, result.Count);
        Assert.Equal("Question part 1\n\npart 2", result[0].Content);
        Assert.Equal(new[] { ChatRole.User, ChatRole.Assistant, ChatRole.User }, result.Select(m => m.Role));
    }
}
