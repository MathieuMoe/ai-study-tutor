using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TutorForge.Core.Abstractions;
using TutorForge.Core.Models;
using TutorForge.Infrastructure.OpenAi;

namespace TutorForge.Tests;

public class OpenAiCompatibleTutorTests
{
    private const string Stream =
        """
        data: {"id":"c1","choices":[{"index":0,"delta":{"role":"assistant","content":""}}]}

        data: {"id":"c1","choices":[{"index":0,"delta":{"content":"What do "}}]}

        data: {"id":"c1","choices":[{"index":0,"delta":{"content":"you know?"}}]}

        data: {"id":"c1","choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}

        data: [DONE]

        """;

    private static (OpenAiCompatibleTutor Tutor, StubHandler Handler) Create(HttpStatusCode status, string body)
    {
        var handler = new StubHandler(status, body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/v1/") };
        var options = new OpenAiCompatibleOptions { ApiKey = "test", Model = "gpt-test" };
        return (new OpenAiCompatibleTutor(http, options, "ChatGPT", NullLogger<OpenAiCompatibleTutor>.Instance), handler);
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
    public async Task Streams_the_content_deltas()
    {
        var (tutor, _) = Create(HttpStatusCode.OK, Stream);
        Assert.Equal("What do you know?", await Collect(tutor.StreamReplyAsync(TestData.Request())));
        Assert.Equal("ChatGPT", tutor.EngineName);
    }

    [Fact]
    public async Task Sends_system_prompt_first_then_the_normalized_history()
    {
        var (tutor, handler) = Create(HttpStatusCode.OK, Stream);
        var request = TestData.Request() with
        {
            Messages = [new ChatMessage(ChatRole.Assistant, "Welcome!"), new ChatMessage(ChatRole.User, "Hi")],
        };

        await Collect(tutor.StreamReplyAsync(request));

        Assert.Equal("/v1/chat/completions", handler.LastUri?.AbsolutePath);
        using var sent = JsonDocument.Parse(handler.LastBody!);
        var root = sent.RootElement;
        Assert.Equal("gpt-test", root.GetProperty("model").GetString());
        Assert.True(root.GetProperty("stream").GetBoolean());
        var roles = root.GetProperty("messages").EnumerateArray().Select(m => m.GetProperty("role").GetString()).ToList();
        Assert.Equal(new[] { "system", "user" }, roles);
    }

    [Fact]
    public async Task Api_errors_become_tutor_exceptions()
    {
        var (tutor, _) = Create(HttpStatusCode.Unauthorized, """{ "error": { "message": "Invalid API key" } }""");
        var ex = await Assert.ThrowsAsync<TutorException>(() => Collect(tutor.StreamReplyAsync(TestData.Request())));
        Assert.Contains("401", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Error_events_in_the_stream_are_reported()
    {
        Assert.Throws<TutorException>(() => OpenAiCompatibleTutor.ParseEvent("data: {\"error\":{\"message\":\"overloaded\"}}"));
    }

    [Theory]
    [InlineData("data: [DONE]")]
    [InlineData(": keep-alive")]
    [InlineData("")]
    public void Non_content_lines_are_ignored(string line) => Assert.Null(OpenAiCompatibleTutor.ParseEvent(line));

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "text/event-stream") };
        }
    }
}
