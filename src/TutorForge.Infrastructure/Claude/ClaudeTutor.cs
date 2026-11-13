using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TutorForge.Core.Abstractions;
using TutorForge.Core.Models;
using TutorForge.Core.Prompts;
using TutorForge.Core.Validation;

namespace TutorForge.Infrastructure.Claude;

/// <summary>
/// Tutor powered by the Anthropic Messages API with streaming (server-sent events):
/// the student sees the answer appear word by word instead of waiting for the full reply.
/// </summary>
public sealed partial class ClaudeTutor(HttpClient httpClient, IOptions<ClaudeOptions> options, ILogger<ClaudeTutor> logger) : ITutor
{
    private readonly ClaudeOptions _options = options.Value;

    public string EngineName => "Claude";

    public async IAsyncEnumerable<string> StreamReplyAsync(TutorRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var errors = TutorRequestValidator.Check(request);
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors.Select(e => e.Message)), nameof(request));
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
        {
            Content = JsonContent.Create(BuildRequestBody(request, _options)),
        };

        LogRequest(logger, _options.Model, request.Mode, request.Messages.Count);

        using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            LogApiError(logger, (int)response.StatusCode, body);
            throw new TutorException($"The AI service returned an error ({(int)response.StatusCode}). Please try again later.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            var text = ParseEvent(line);
            if (text is not null)
            {
                yield return text;
            }
        }
    }

    internal static JsonObject BuildRequestBody(TutorRequest request, ClaudeOptions options)
    {
        var messages = new JsonArray();
        foreach (var m in ConversationNormalizer.Normalize(request.Messages))
        {
            messages.Add(new JsonObject
            {
                ["role"] = m.Role == ChatRole.User ? "user" : "assistant",
                ["content"] = m.Content,
            });
        }

        return new JsonObject
        {
            ["model"] = options.Model,
            ["max_tokens"] = options.MaxTokens,
            ["system"] = TutorPromptBuilder.BuildSystemPrompt(request),
            ["messages"] = messages,
            ["stream"] = true,
        };
    }

    /// <summary>
    /// Parses one line of the SSE stream. Returns the text of a <c>content_block_delta</c>,
    /// null for every other line, and throws on an <c>error</c> event.
    /// </summary>
    internal static string? ParseEvent(string line)
    {
        if (!line.StartsWith("data:", StringComparison.Ordinal))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(line.AsMemory(5));
        var root = doc.RootElement;
        var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;

        if (type == "content_block_delta" && root.TryGetProperty("delta", out var delta)
            && delta.TryGetProperty("text", out var text))
        {
            return text.GetString();
        }

        if (type == "error")
        {
            throw new TutorException("The AI service interrupted the answer. Please try again.");
        }

        return null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Tutor request to {Model}: mode {Mode}, {Count} messages")]
    private static partial void LogRequest(ILogger logger, string model, TutorMode mode, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Anthropic API error {Status}: {Body}")]
    private static partial void LogApiError(ILogger logger, int status, string body);
}
