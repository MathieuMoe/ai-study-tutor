using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TutorForge.Core.Abstractions;
using TutorForge.Infrastructure.Claude;
using TutorForge.Infrastructure.Demo;
using TutorForge.Infrastructure.Gemini;
using TutorForge.Infrastructure.OpenAi;

namespace TutorForge.Infrastructure.Engines;

/// <summary>An AI engine the student can choose in the interface.</summary>
public sealed record TutorEngine(string Id, string Name, bool IsDemo, Func<ITutor> Create);

/// <summary>
/// The AI engines available with the current configuration (each provider appears when its key is set),
/// plus the offline demo, which is always available. The interface lets the user pick one.
/// </summary>
public sealed class TutorEngines
{
    public const string ClaudeId = "claude";
    public const string OpenAiId = "openai";
    public const string GeminiId = "gemini";
    public const string OllamaId = "ollama";
    public const string DemoId = "demo";

    private readonly Dictionary<string, TutorEngine> _byId;

    public TutorEngines(
        IHttpClientFactory httpClientFactory,
        IOptions<ClaudeOptions> claude,
        IOptionsMonitor<OpenAiCompatibleOptions> openAi,
        IOptions<AiOptions> ai,
        DemoTutor demo,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(claude);
        ArgumentNullException.ThrowIfNull(openAi);
        ArgumentNullException.ThrowIfNull(ai);
        loggerFactory ??= NullLoggerFactory.Instance;
        var engines = new List<TutorEngine>();

        if (!string.IsNullOrWhiteSpace(claude.Value.ApiKey))
        {
            engines.Add(new(ClaudeId, "Claude", false, () =>
                new ClaudeTutor(httpClientFactory.CreateClient(ClaudeId), claude, loggerFactory.CreateLogger<ClaudeTutor>())));
        }

        foreach (var (id, name, section) in new[]
        {
            (OpenAiId, "ChatGPT", OpenAiCompatibleOptions.OpenAi),
            (GeminiId, "Gemini", OpenAiCompatibleOptions.Gemini),
            (OllamaId, "Ollama (local)", OpenAiCompatibleOptions.Ollama),
        })
        {
            var options = openAi.Get(section);
            if (options.IsConfigured)
            {
                engines.Add(new(id, name, false, id == GeminiId
                    ? () => new GeminiTutor(httpClientFactory.CreateClient(id), options, loggerFactory.CreateLogger<GeminiTutor>())
                    : () => new OpenAiCompatibleTutor(httpClientFactory.CreateClient(id), options, name, loggerFactory.CreateLogger<OpenAiCompatibleTutor>())));
            }
        }

        engines.Add(new(DemoId, demo.EngineName, true, () => demo));

        All = engines;
        _byId = engines.ToDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase);
        Default = (ai.Value.Default is { } preferred && _byId.TryGetValue(preferred, out var chosen)) ? chosen : engines[0];
    }

    public IReadOnlyList<TutorEngine> All { get; }

    public TutorEngine Default { get; }

    /// <summary>The engine with this id, or the default one when the id is unknown or not configured.</summary>
    public TutorEngine Get(string? id) => id is not null && _byId.TryGetValue(id, out var engine) ? engine : Default;
}

/// <summary>"Ai" section: which engine is selected by default (claude, openai, gemini, ollama or demo).</summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    public string? Default { get; set; }
}
