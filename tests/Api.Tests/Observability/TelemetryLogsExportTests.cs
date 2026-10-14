using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Logs;
using Perezosoft.Api.Observability;

namespace Perezosoft.Api.Tests.Observability;

/// <summary>
/// OBS-2 (ADR-008 amendment, 2026-09-07): log records are exported alongside traces and metrics. The
/// OpenTelemetry logger provider is registered by <c>AddAppTelemetry</c>, and each record carries the
/// rendered message + the logging scopes (the per-request tenant/user scope) — without these an error's
/// text lives only on the host's stdout while its trace sits alone in the collector.
/// </summary>
public class TelemetryLogsExportTests
{
    private static ServiceProvider Build(string? endpoint)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OpenTelemetry:Otlp:Endpoint"] = endpoint })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAppTelemetry(config);
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(null, "Perezosoft.Api")]       // the platform's default brand
    [InlineData("Vuelto.Api", "Vuelto.Api")]   // a host's own (Brand.ServiceName, Pkg 3.9b #433)
    public void TracesAreFiledUnderTheHostsServiceName(string? serviceName, string expected)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OpenTelemetry:Otlp:Endpoint"] = "https://collector.example/otlp",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        if (serviceName is null) services.AddAppTelemetry(config); else services.AddAppTelemetry(config, serviceName);
        using var sp = services.BuildServiceProvider();

        var resource = OpenTelemetry.ProviderExtensions.GetResource(sp.GetRequiredService<OpenTelemetry.Trace.TracerProvider>());
        Assert.Contains(resource.Attributes, a => a.Key == "service.name" && (string)a.Value == expected);
    }

    [Fact]
    public void WithAnEndpoint_TheOpenTelemetryLogProvider_IsRegistered_WithMessageAndScopes()
    {
        using var sp = Build("https://collector.example/otlp");

        Assert.Contains(sp.GetServices<ILoggerProvider>(), p => p is OpenTelemetryLoggerProvider);
        Assert.NotNull(sp.GetService<OpenTelemetry.Metrics.MeterProvider>()); // runtime + Npgsql meters ride the same pipeline
        var options = sp.GetRequiredService<IOptions<OpenTelemetryLoggerOptions>>().Value;
        Assert.True(options.IncludeFormattedMessage);
        Assert.True(options.IncludeScopes);
        Assert.True(options.ParseStateValues);
    }

    [Fact]
    public void WithoutAnEndpoint_TheAppStillBoots_AndLogsStillWork()
    {
        using var sp = Build(null);

        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("probe");
        logger.LogInformation("no exporter configured — nothing leaves the process");
        Assert.NotNull(logger);
    }
}
