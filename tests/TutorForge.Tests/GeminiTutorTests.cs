using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TutorForge.Core.Abstractions;
using TutorForge.Core.Models;
using TutorForge.Infrastructure.Gemini;
using TutorForge.Infrastructure.OpenAi;

namespace TutorForge.Tests;

public class GeminiTutorTests
{
    private static TutorRequest Request() => TestData.Request();

    private sealed class FakeHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "text/event-stream") };
        }
    }

    private static (GeminiTutor Tutor, FakeHandler Handler) Create(HttpStatusCode status, string body)
    {
        var handler = new FakeHandler(status, body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/") };
        return (new GeminiTutor(http, new OpenAiCompatibleOptions { ApiKey = "AQ.test", Model = "gemini-flash-latest" }, NullLogger<GeminiTutor>.Instance), handler);
    }

    [Fact]
    public async Task Streams_the_text_parts_from_the_native_api()
    {
        const string sse = "data: {\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[{\"text\":\"A cell \"}]}}]}\n\n"
                         + "data: {\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[{\"text\":\"is life.\"}]},\"finishReason\":\"STOP\"}]}\n\n";
        var (tutor, handler) = Create(HttpStatusCode.OK, sse);

        var chunks = new List<string>();
        await foreach (var c in tutor.StreamReplyAsync(Request()))
        {
            chunks.Add(c);
        }

        Assert.Equal("A cell is life.", string.Concat(chunks));
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-flash-latest:streamGenerateContent?alt=sse", handler.LastRequestUri?.ToString());
        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.True(body.RootElement.TryGetProperty("systemInstruction", out _));
        Assert.Equal("user", body.RootElement.GetProperty("contents")[0].GetProperty("role").GetString());
    }

    [Fact]
    public async Task An_api_error_becomes_a_tutor_exception()
    {
        var (tutor, _) = Create(HttpStatusCode.BadRequest, "{\"error\":{\"code\":400,\"message\":\"API key not valid\"}}");

        await Assert.ThrowsAsync<TutorException>(async () =>
        {
            await foreach (var chunk in tutor.StreamReplyAsync(Request()))
            {
                Assert.Fail(chunk);
            }
        });
    }

    [Fact]
    public void Thought_parts_and_other_lines_are_ignored()
    {
        Assert.Null(GeminiTutor.ParseEvent(""));
        Assert.Null(GeminiTutor.ParseEvent(": keep-alive"));
        Assert.Equal("Hi", GeminiTutor.ParseEvent("data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"thinking\",\"thought\":true},{\"text\":\"Hi\"}]}}]}"));
    }
}
