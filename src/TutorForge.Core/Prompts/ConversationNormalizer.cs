using TutorForge.Core.Models;

namespace TutorForge.Core.Prompts;

/// <summary>
/// Prepares the history for a chat API: it must start with the student and alternate roles.
/// Drops leading tutor messages (e.g. the welcome message), merges consecutive messages of the same role
/// and removes empty ones.
/// </summary>
public static class ConversationNormalizer
{
    public static IReadOnlyList<ChatMessage> Normalize(IEnumerable<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var result = new List<ChatMessage>();

        foreach (var message in messages)
        {
            var content = message.Content?.Trim();
            if (string.IsNullOrEmpty(content) || (result.Count == 0 && message.Role != ChatRole.User))
            {
                continue;
            }

            if (result.Count > 0 && result[^1].Role == message.Role)
            {
                result[^1] = result[^1] with { Content = result[^1].Content + "\n\n" + content };
            }
            else
            {
                result.Add(new ChatMessage(message.Role, content));
            }
        }

        return result;
    }
}
