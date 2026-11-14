using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TutorForge.Core.Abstractions;
using TutorForge.Core.Models;
using TutorForge.Core.Prompts;
using TutorForge.Core.Validation;
using TutorForge.Infrastructure.OpenAi;

namespace TutorForge.Infrastructure.Gemini;

/// <summary>
/// Tutor for Google Gemini using the native API (streamGenerateContent with server-sent events).
/// The native API accepts the new "AQ." keys through the x-goog-api-key header, which the OpenAI-compatible endpoint may reject.
/// </summary>
public sealed partial class GeminiTutor(
    HttpClient httpClient,
    OpenAiCompatibleOptions options,
    ILogger<GeminiTutor> logger) : ITutor
{
    public string EngineName => "Gemini";

    public async IAsyncEnumerable<string> StreamReplyAsync(TutorRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var errors = TutorRequestValidator.Check(request);
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors.Select(e => e.Message)), nameof(request));
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, $"models/{Uri.EscapeDataString(options.Model)}:streamGenerateContent?alt=sse")
        {
            Content = new StringContent(BuildRequestBody(request).ToJsonString(), Encoding.UTF8, "application/json"),
        };

        LogRequest(logger, options.Model, request.Mode);

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
            if (!string.IsNullOrEmpty(text))
            {
                yield return text;
            }
        }
    }

    internal static JsonObject BuildRequestBody(TutorRequest request)
    {
        var contents = new JsonArray();
        foreach (var m in ConversationNormalizer.Normalize(request.Messages))
        {
            contents.Add(new JsonObject
            {
                ["role"] = m.Role == ChatRole.User ? "user" : "model",
                ["parts"] = new JsonArray { new JsonObject { ["text"] = m.Content } },
            });
        }

        return new JsonObject
        {
            ["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray { new JsonObject { ["text"] = TutorPromptBuilder.BuildSystemPrompt(request) } },
            },
            ["contents"] = contents,
        };
    }

    /// <summary>Parses one SSE line: "data: {candidates:[{content:{parts:[{text}]}}]}".</summary>
    internal static string? ParseEvent(string line)
    {
        if (!line.StartsWith("data:", StringComparison.Ordinal))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(line.AsSpan(5).Trim().ToString());
        var root = doc.RootElement;
        if (root.TryGetProperty("error", out _))
        {
            throw new TutorException("The AI service interrupted the answer. Please try again.");
        }

        if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0
            || !candidates[0].TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts)
            || parts.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var text = new StringBuilder();
        foreach (var part in parts.EnumerateArray())
        {
            var isThought = part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True;
            if (!isThought && part.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
            {
                text.Append(t.GetString());
            }
        }

        return text.Length == 0 ? null : text.ToString();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Tutor request to Gemini ({Model}): mode {Mode}")]
    private static partial void LogRequest(ILogger logger, string model, TutorMode mode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Gemini API error {Status}: {Body}")]
    private static partial void LogApiError(ILogger logger, int status, string body);
}
