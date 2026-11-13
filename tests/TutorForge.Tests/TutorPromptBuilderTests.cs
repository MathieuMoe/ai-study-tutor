using TutorForge.Core.Models;
using TutorForge.Core.Prompts;

namespace TutorForge.Tests;

public class TutorPromptBuilderTests
{
    [Fact]
    public void Prompt_contains_subject_level_and_language()
    {
        var prompt = TutorPromptBuilder.BuildSystemPrompt(TestData.Request(language: TutorLanguage.Catalan));

        Assert.Contains("Mathematics", prompt, StringComparison.Ordinal);
        Assert.Contains("secondary school", prompt, StringComparison.Ordinal);
        Assert.Contains("Always answer in Catalan", prompt, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TutorMode.Guide, "Socratic")]
    [InlineData(TutorMode.Explain, "Mode EXPLAIN")]
    [InlineData(TutorMode.Check, "Mode CHECK MY ANSWER")]
    [InlineData(TutorMode.Practice, "Mode PRACTICE")]
    public void Each_mode_has_its_own_pedagogy(TutorMode mode, string expected)
    {
        Assert.Contains(expected, TutorPromptBuilder.BuildSystemPrompt(TestData.Request(mode: mode)), StringComparison.Ordinal);
    }

    [Fact]
    public void Guide_mode_does_not_give_the_answer_straight_away()
    {
        Assert.Contains("Do NOT give the final answer", TutorPromptBuilder.BuildSystemPrompt(TestData.Request()), StringComparison.Ordinal);
    }

    [Fact]
    public void Safety_rules_are_always_included()
    {
        var prompt = TutorPromptBuilder.BuildSystemPrompt(TestData.Request(mode: TutorMode.Practice));
        Assert.Contains("trusted adult", prompt, StringComparison.Ordinal);
        Assert.Contains("Never ask for personal information", prompt, StringComparison.Ordinal);
    }
}
