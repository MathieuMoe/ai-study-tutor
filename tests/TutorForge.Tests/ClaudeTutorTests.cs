using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TutorForge.Core.Abstractions;
using TutorForge.Core.Models;
using TutorForge.Infrastructure.Claude;

namespace TutorForge.Tests;

public class ClaudeTutorTests
{
    private const string Stream =
        """
        event: message_start
        data: {"type":"message_start","message":{"id":"msg_1"}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"What do "}}

        event: ping
        data: {"type":"ping"}

        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"you know?"}}

        event: message_stop
        data: {"type":"message_stop"}

        """;

    private static (ClaudeTutor Tutor, FakeHandler Handler) Create(HttpStatusCode status, string body)
    {
        var handler = new FakeHandler(status, body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com/") };
        return (new ClaudeTutor(http, Options.Create(new ClaudeOptions { ApiKey = "test" }), NullLogger<ClaudeTutor>.Instance), handler);
    }

    private static async Task<string> Collect(IAsyncEnumerable<string> chunks)
    {
        var sb = new StringBuilder();
        await foreach (var c in chunks)
        {
            sb.Append(c);
        }

        return sb.ToString();
    }

    [Fact]
    public async Task Streams_the_text_deltas()
    {
        var (tutor, _) = Create(HttpStatusCode.OK, Stream);
        Assert.Equal("What do you know?", await Collect(tutor.StreamReplyAsync(TestData.Request())));
    }

    [Fact]
    public async Task Sends_a_streaming_request_with_system_prompt_and_alternating_history()
    {
        var (tutor, handler) = Create(HttpStatusCode.OK, Stream);
        var request = TestData.Request() with
        {
            Messages =
            [
                new ChatMessage(ChatRole.Assistant, "Welcome!"),
                new ChatMessage(ChatRole.User, "Hi"),
                new ChatMessage(ChatRole.Assistant, "Hello, what are you working on?"),
                new ChatMessage(ChatRole.User, "Fractions"),
            ],
        };

        await Collect(tutor.StreamReplyAsync(request));

        using var sent = JsonDocument.Parse(handler.LastRequestBody!);
        var root = sent.RootElement;
        Assert.True(root.GetProperty("stream").GetBoolean());
        Assert.Contains("Socratic", root.GetProperty("system").GetString(), StringComparison.Ordinal);
        var roles = root.GetProperty("messages").EnumerateArray().Select(m => m.GetProperty("role").GetString()).ToList();
        Assert.Equal(new[] { "user", "assistant", "user" }, roles);
    }

    [Fact]
    public async Task Api_errors_become_tutor_exceptions()
    {
        var (tutor, _) = Create(HttpStatusCode.TooManyRequests, """{ "error": { "type": "rate_limit_error" } }""");
        var ex = await Assert.ThrowsAsync<TutorException>(() => Collect(tutor.StreamReplyAsync(TestData.Request())));
        Assert.Contains("429", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Error_events_in_the_stream_are_reported()
    {
        var body = "event: error\ndata: {\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\"}}\n\n";
        var (tutor, _) = Create(HttpStatusCode.OK, body);
        await Assert.ThrowsAsync<TutorException>(() => Collect(tutor.StreamReplyAsync(TestData.Request())));
    }

    [Fact]
    public async Task Invalid_requests_are_rejected_before_calling_the_api()
    {
        var (tutor, handler) = Create(HttpStatusCode.OK, Stream);
        await Assert.ThrowsAsync<ArgumentException>(() => Collect(tutor.StreamReplyAsync(new TutorRequest([]))));
        Assert.Null(handler.LastRequestBody);
    }

    private sealed class FakeHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "text/event-stream") };
        }
    }
}
