using Microsoft.Extensions.Options;
using TutorForge.Infrastructure.Claude;
using TutorForge.Infrastructure.Demo;
using TutorForge.Infrastructure.Engines;
using TutorForge.Infrastructure.OpenAi;

namespace TutorForge.Tests;

public class TutorEnginesTests
{
    private sealed class Factory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class Monitor(Dictionary<string, OpenAiCompatibleOptions> byName) : IOptionsMonitor<OpenAiCompatibleOptions>
    {
        public OpenAiCompatibleOptions CurrentValue => new();

        public OpenAiCompatibleOptions Get(string? name) => name is not null && byName.TryGetValue(name, out var o) ? o : new();

        public IDisposable? OnChange(Action<OpenAiCompatibleOptions, string?> listener) => null;
    }

    private static TutorEngines Create(string? claudeKey = null, string? openAiKey = null, bool ollama = false, string? preferred = null) =>
        new(new Factory(),
            Options.Create(new ClaudeOptions { ApiKey = claudeKey }),
            new Monitor(new()
            {
                [OpenAiCompatibleOptions.OpenAi] = new() { ApiKey = openAiKey, Model = "gpt-test" },
                [OpenAiCompatibleOptions.Ollama] = new() { Enabled = ollama, Model = "llama" },
            }),
            Options.Create(new AiOptions { Default = preferred }),
            new DemoTutor());

    [Fact]
    public void Without_keys_only_the_demo_is_available()
    {
        var engines = Create();
        var only = Assert.Single(engines.All);
        Assert.True(only.IsDemo);
        Assert.Same(only, engines.Default);
    }

    [Fact]
    public void Each_configured_provider_is_offered_and_the_demo_stays_last()
    {
        var engines = Create(claudeKey: "k1", openAiKey: "k2", ollama: true);
        Assert.Equal(new[] { "claude", "openai", "ollama", "demo" }, engines.All.Select(e => e.Id));
        Assert.Equal("claude", engines.Default.Id);
    }

    [Fact]
    public void The_preferred_default_is_used_when_configured()
    {
        Assert.Equal("openai", Create(claudeKey: "k1", openAiKey: "k2", preferred: "openai").Default.Id);
    }

    [Theory]
    [InlineData("gemini")] // not configured
    [InlineData("nope")]
    [InlineData("")]
    public void Unknown_or_unavailable_ids_fall_back_to_the_default(string? id)
    {
        var engines = Create(openAiKey: "k");
        Assert.Equal("openai", engines.Get(id).Id);
    }

    [Fact]
    public void Engines_create_the_right_tutor()
    {
        var engines = Create(openAiKey: "k");
        Assert.IsType<OpenAiCompatibleTutor>(engines.Get("openai").Create());
        Assert.IsType<DemoTutor>(engines.Get("demo").Create());
    }
}
