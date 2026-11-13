using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TutorForge.Core.Abstractions;
using TutorForge.Infrastructure.Claude;
using TutorForge.Infrastructure.Demo;

namespace TutorForge.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the tutor: Claude when an API key is configured (Claude:ApiKey or ANTHROPIC_API_KEY),
    /// otherwise the offline demo tutor.
    /// </summary>
    public static IServiceCollection AddTutorForge(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ClaudeOptions>()
            .Bind(configuration.GetSection(ClaudeOptions.SectionName))
            .PostConfigure(o => o.ApiKey ??= configuration["ANTHROPIC_API_KEY"]);

        var apiKey = configuration[$"{ClaudeOptions.SectionName}:ApiKey"] ?? configuration["ANTHROPIC_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            services.AddSingleton<ITutor, DemoTutor>();
            return services;
        }

        services.AddHttpClient<ITutor, ClaudeTutor>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<ClaudeOptions>>().Value;
            client.BaseAddress = options.BaseUrl;
            client.Timeout = options.Timeout;
            client.DefaultRequestHeaders.Add("x-api-key", options.ApiKey);
            client.DefaultRequestHeaders.Add("anthropic-version", options.ApiVersion);
        });

        return services;
    }
}
