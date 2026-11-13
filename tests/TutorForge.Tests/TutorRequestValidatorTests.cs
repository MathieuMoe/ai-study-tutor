using TutorForge.Core.Models;
using TutorForge.Core.Validation;

namespace TutorForge.Tests;

public class TutorRequestValidatorTests
{
    [Fact]
    public void Valid_request_has_no_errors() => Assert.Empty(TutorRequestValidator.Check(TestData.Request()));

    [Fact]
    public void Empty_conversation_is_rejected()
    {
        var error = Assert.Single(TutorRequestValidator.Check(new TutorRequest([])));
        Assert.Equal(TutorRequestValidator.Empty, error.Code);
    }

    [Fact]
    public void Last_message_must_come_from_the_student()
    {
        var request = TestData.Request() with
        {
            Messages = [new ChatMessage(ChatRole.User, "Hi"), new ChatMessage(ChatRole.Assistant, "Hello!")],
        };
        Assert.Contains(TutorRequestValidator.Check(request), e => e.Code == TutorRequestValidator.LastMustBeUser);
    }

    [Fact]
    public void Too_long_message_is_rejected()
    {
        var request = TestData.Request(new string('a', TutorRequest.MaxMessageLength + 1));
        Assert.Contains(TutorRequestValidator.Check(request), e => e.Code == TutorRequestValidator.MessageTooLong);
    }

    [Fact]
    public void Too_long_conversation_is_rejected()
    {
        var messages = Enumerable.Range(0, TutorRequest.MaxMessages + 1)
            .Select(i => new ChatMessage(i % 2 == 0 ? ChatRole.User : ChatRole.Assistant, "x"))
            .ToList();
        var request = TestData.Request() with { Messages = messages };
        Assert.Contains(TutorRequestValidator.Check(request), e => e.Code == TutorRequestValidator.ConversationTooLong);
    }

    [Fact]
    public void Unknown_options_are_rejected()
    {
        var request = TestData.Request() with { Mode = (TutorMode)99 };
        Assert.Contains(TutorRequestValidator.Check(request), e => e.Code == TutorRequestValidator.UnknownOption);
    }

    [Fact]
    public void Too_long_topic_is_rejected()
    {
        var request = TestData.Request() with { Topic = new string('t', TutorRequest.MaxTopicLength + 1) };
        Assert.Contains(TutorRequestValidator.Check(request), e => e.Code == TutorRequestValidator.TopicTooLong);
    }
}
