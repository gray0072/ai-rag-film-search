using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FilmSearch.UnitTests.AI;

public sealed class AiProviderHealthCheckTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Healthy_when_the_provider_lists_both_active_models()
    {
        var handler = new FakeProviderHandler();
        using var host = AiTestHost.Build(handler);

        var report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(Ct);

        report.Entries["ai-chat"].Status.ShouldBe(HealthStatus.Healthy);
        report.Entries["ai-embeddings"].Status.ShouldBe(HealthStatus.Healthy);
        handler.Requests.ShouldAllBe(r => r.Uri.ToString() == "http://local.test/v1/models" && r.Authorization == "Bearer lm-studio");
    }

    [Fact]
    public async Task Degraded_when_the_model_is_not_listed()
    {
        var handler = new FakeProviderHandler();
        using var host = AiTestHost.Build(handler, new() { ["AI:ChatProfiles:local:Model"] = "missing-model" });

        var report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(Ct);

        report.Entries["ai-chat"].Status.ShouldBe(HealthStatus.Degraded);
        report.Entries["ai-chat"].Description.ShouldNotBeNull().ShouldContain("missing-model");
        report.Status.ShouldBe(HealthStatus.Degraded);
    }

    [Fact]
    public async Task A_profile_can_read_its_model_list_from_another_path()
    {
        var handler = new FakeProviderHandler();
        using var host = AiTestHost.Build(handler, new() { ["AI:EmbeddingProfiles:local:ModelsPath"] = "/embeddings/models" });

        var report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(Ct);

        report.Entries["ai-embeddings"].Status.ShouldBe(HealthStatus.Healthy);
        handler.Requests.Select(r => r.Uri.ToString()).ShouldBe(["http://local.test/v1/models", "http://local.test/v1/embeddings/models"], ignoreOrder: true);
    }

    [Fact]
    public async Task Degraded_not_unhealthy_when_the_provider_is_down()
    {
        var handler = new FakeProviderHandler { Respond = _ => throw new HttpRequestException("Connection refused") };
        using var host = AiTestHost.Build(handler);

        var report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(Ct);

        report.Status.ShouldBe(HealthStatus.Degraded);
        report.Entries["ai-chat"].Description.ShouldNotBeNull().ShouldContain("not reachable");
    }

    [Fact]
    public async Task Health_checks_can_be_switched_off()
    {
        using var host = AiTestHost.Build(new FakeProviderHandler(), new() { ["AI:HealthChecksEnabled"] = "false" });

        var report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(Ct);

        report.Entries.ShouldNotContainKey("ai-chat");
    }
}
