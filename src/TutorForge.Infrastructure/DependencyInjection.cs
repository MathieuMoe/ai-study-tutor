using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TutorForge.Core.Abstractions;
using TutorForge.Core.Progress;
using TutorForge.Infrastructure.Claude;
using TutorForge.Infrastructure.Demo;
using TutorForge.Infrastructure.Engines;
using TutorForge.Infrastructure.OpenAi;
using TutorForge.Infrastructure.Progress;
using TutorForge.Infrastructure.Records;
using TutorForge.Core.Records;

namespace TutorForge.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the progress store and the AI engines. Claude, ChatGPT, Gemini and Ollama are offered when configured;
    /// the offline demo is always available. The user picks the engine in the interface.
    /// </summary>
    public static IServiceCollection AddTutorForge(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Student progress (anonymous). Swap FileStudentProgressStore for a SQL implementation in production.
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<ProgressStoreOptions>().Bind(configuration.GetSection(ProgressStoreOptions.SectionName));
        services.AddSingleton<IStudentProgressStore, FileStudentProgressStore>();
        // Student record ("expediente"): profile, courses, results, activity, goals, teacher notes. JSON in the demo.
        services.AddSingleton<IStudentRecordStore, FileStudentRecordStore>();
        services.AddSingleton<ProgressService>();
        services.AddHostedService<ProgressCleanupService>();

        // ----- AI engines: Claude, ChatGPT, Gemini, Ollama (local) and the offline demo -----
        // Each provider becomes available when its key is configured (user-secrets or environment variables).
        services.AddOptions<AiOptions>().Bind(configuration.GetSection(AiOptions.SectionName));

        services.AddOptions<ClaudeOptions>()
            .Bind(configuration.GetSection(ClaudeOptions.SectionName))
            .PostConfigure(o => o.ApiKey ??= configuration["ANTHROPIC_API_KEY"]);

        AddOpenAiCompatible(services, configuration, OpenAiCompatibleOptions.OpenAi, TutorEngines.OpenAiId,
            "gpt-5-mini", "https://api.openai.com/v1/", "OPENAI_API_KEY");
        AddOpenAiCompatible(services, configuration, OpenAiCompatibleOptions.Gemini, TutorEngines.GeminiId,
            "gemini-flash-latest", "https://generativelanguage.googleapis.com/v1beta/", "GEMINI_API_KEY", googleKeyHeader: true);
        AddOpenAiCompatible(services, configuration, OpenAiCompatibleOptions.Ollama, TutorEngines.OllamaId,
            "llama3.2", "http://localhost:11434/v1/", apiKeyVariable: null);

        services.AddHttpClient(TutorEngines.ClaudeId, (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<ClaudeOptions>>().Value;
            client.BaseAddress = options.BaseUrl;
            client.Timeout = options.Timeout;
            if (!string.IsNullOrWhiteSpace(options.ApiKey))
            {
                client.DefaultRequestHeaders.Add("x-api-key", options.ApiKey);
            }

            client.DefaultRequestHeaders.Add("anthropic-version", options.ApiVersion);
        });

        services.AddSingleton<DemoTutor>();
        services.AddScoped<TutorEngines>();

        return services;
    }

    private static void AddOpenAiCompatible(IServiceCollection services, IConfiguration configuration,
        string section, string clientName, string defaultModel, string defaultBaseUrl, string? apiKeyVariable, bool googleKeyHeader = false)
    {
        services.AddOptions<OpenAiCompatibleOptions>(section)
            .Configure(o =>
            {
                o.Model = defaultModel;
                o.BaseUrl = new Uri(defaultBaseUrl);
            })
            .Bind(configuration.GetSection(section))
            .PostConfigure(o =>
            {
                if (apiKeyVariable is not null)
                {
                    o.ApiKey ??= configuration[apiKeyVariable];
                }
            });

        services.AddHttpClient(clientName, (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptionsMonitor<OpenAiCompatibleOptions>>().Get(section);
            client.BaseAddress = options.BaseUrl;
            client.Timeout = options.Timeout;
            if (!string.IsNullOrWhiteSpace(options.ApiKey) && googleKeyHeader)
            {
                // Native Gemini API: works with both the old "AIza" keys and the new "AQ." keys.
                client.DefaultRequestHeaders.Add("x-goog-api-key", options.ApiKey);
            }
            else if (!string.IsNullOrWhiteSpace(options.ApiKey))
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", options.ApiKey);
            }
        });
    }
}
