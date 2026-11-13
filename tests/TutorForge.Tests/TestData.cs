using TutorForge.Core.Models;

namespace TutorForge.Tests;

internal static class TestData
{
    public static TutorRequest Request(string message = "How do I solve 2x + 3 = 11?", TutorMode mode = TutorMode.Guide,
        TutorLanguage language = TutorLanguage.English) =>
        new([new ChatMessage(ChatRole.User, message)], Subject.Mathematics, Level.Secondary, mode, language);
}
