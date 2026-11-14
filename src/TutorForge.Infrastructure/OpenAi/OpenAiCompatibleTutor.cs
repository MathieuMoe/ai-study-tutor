using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TutorForge.Core.Abstractions;
using TutorForge.Core.Models;
using TutorForge.Core.Prompts;
using TutorForge.Core.Validation;

namespace TutorForge.Infrastructure.OpenAi;

/// <summary>
/// Tutor for OpenAI-compatible APIs (ChatGPT, Gemini, Ollama) using the Chat Completions endpoint with streaming.
/// Same prompts and pedagogy as the Claude tutor: only the transport changes.
/// </summary>
public sealed partial class OpenAiCompatibleTutor(
    HttpClient httpClient,
    OpenAiCompatibleOptions options,
    string engineName,
    ILogger<OpenAiCompatibleTutor> logger) : ITutor
{
    public string EngineName => engineName;

    public async IAsyncEnumerable<string> StreamReplyAsync(TutorRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var errors = TutorRequestValidator.Check(request);
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors.Select(e => e.Message)), nameof(request));
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            // Buffered body with Content-Length: some OpenAI-compatible servers and proxies reject chunked requests.
            Content = new StringContent(BuildRequestBody(request, options).ToJsonString(), System.Text.Encoding.UTF8, "application/json"),
        };

        LogRequest(logger, engineName, options.Model, request.Mode);

        using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            LogApiError(logger, engineName, (int)response.StatusCode, body);
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

    internal static JsonObject BuildRequestBody(TutorRequest request, OpenAiCompatibleOptions options)
    {
        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = TutorPromptBuilder.BuildSystemPrompt(request) },
        };

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
            ["messages"] = messages,
            ["stream"] = true,
        };
    }

    /// <summary>Parses one SSE line: "data: {choices:[{delta:{content}}]}" or "data: [DONE]".</summary>
    internal static string? ParseEvent(string line)
    {
        if (!line.StartsWith("data:", StringComparison.Ordinal))
        {
            return null;
        }

        var payload = line.AsSpan(5).Trim();
        if (payload.SequenceEqual("[DONE]"))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(payload.ToString());
        var root = doc.RootElement;
        if (root.TryGetProperty("error", out _))
        {
            throw new TutorException("The AI service interrupted the answer. Please try again.");
        }

        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0
            && choices[0].TryGetProperty("delta", out var delta)
            && delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
        {
            return content.GetString();
        }

        return null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Tutor request to {Engine} ({Model}): mode {Mode}")]
    private static partial void LogRequest(ILogger logger, string engine, string model, TutorMode mode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Engine} API error {Status}: {Body}")]
    private static partial void LogApiError(ILogger logger, string engine, int status, string body);
}
