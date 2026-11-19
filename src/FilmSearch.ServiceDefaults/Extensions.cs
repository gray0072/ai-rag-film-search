using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Common Aspire services: service discovery, resilience, health checks and OpenTelemetry.
/// Every service project (Api, Worker) calls <see cref="AddServiceDefaults{TBuilder}"/>.
/// </summary>
public static class Extensions
{
    public const string HealthEndpointPath = "/health";
    public const string AlivenessEndpointPath = "/alive";

    /// <summary>Meter and activity source name for custom FilmSearch telemetry (spec 10 §3).</summary>
    public const string FilmSearchTelemetryName = "FilmSearch";

    /// <summary>Activity source and meter of the Microsoft.Extensions.AI GenAI telemetry (FilmSearch.AI, spec 10 §3).</summary>
    public const string AiTelemetryName = "FilmSearch.AI";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(FilmSearchTelemetryName)
                .AddMeter(AiTelemetryName))
            .WithTracing(tracing => tracing
                .AddSource(builder.Environment.ApplicationName)
                .AddSource(FilmSearchTelemetryName)
                .AddSource(AiTelemetryName)
                .AddAspNetCoreInstrumentation(options =>
                    // Health probes are noise in traces.
                    options.Filter = context =>
                        !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                        && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath))
                .AddHttpClientInstrumentation());

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        // The AppHost sets OTEL_EXPORTER_OTLP_ENDPOINT to the Aspire dashboard.
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
        var langfuse = LangfuseExport.CreateOptions(builder.Configuration);
        if (langfuse is null)
        {
            if (useOtlpExporter)
            {
                builder.Services.AddOpenTelemetry().UseOtlpExporter();
            }

            return builder;
        }

        // UseOtlpExporter cannot be combined with a second exporter, so with Langfuse every signal gets its own
        // exporter (they read the same OTEL_EXPORTER_OTLP_* variables) and traces get one more for Langfuse.
        var otel = builder.Services.AddOpenTelemetry();
        if (useOtlpExporter)
        {
            builder.Logging.AddOpenTelemetry(logging => logging.AddOtlpExporter());
            otel.WithMetrics(metrics => metrics.AddOtlpExporter());
            otel.WithTracing(tracing => tracing.AddOtlpExporter());
        }

        otel.WithTracing(tracing => tracing.AddProcessor(LangfuseExport.CreateProcessor(langfuse)));

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    /// <summary>
    /// Maps <c>/health</c> (all checks, readiness) and <c>/alive</c> (liveness only).
    /// Mapped in every environment: container platforms need probes. The response is only the status text.
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks(HealthEndpointPath);
        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("live"),
        });

        return app;
    }
}
