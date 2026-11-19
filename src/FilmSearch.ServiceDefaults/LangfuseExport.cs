using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Configuration;
using OpenTelemetry;
using OpenTelemetry.Exporter;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Optional second trace exporter (I18): spans of AI work go to a self-hosted Langfuse through its OTLP endpoint
/// (<c>{Langfuse:BaseUrl}/api/public/otel/v1/traces</c>, HTTP/protobuf, Basic auth with the project keys). The Aspire
/// dashboard still gets everything. Configured by the AppHost when Langfuse is switched on.
/// </summary>
public static class LangfuseExport
{
    public const string BaseUrlKey = "Langfuse:BaseUrl";
    public const string PublicKeyKey = "Langfuse:PublicKey";
    public const string SecretKeyKey = "Langfuse:SecretKey";

    /// <summary>Exporter options for Langfuse, or null when the <c>Langfuse</c> section is incomplete (off).</summary>
    public static OtlpExporterOptions? CreateOptions(IConfiguration configuration)
    {
        var baseUrl = configuration[BaseUrlKey];
        var publicKey = configuration[PublicKeyKey];
        var secretKey = configuration[SecretKeyKey];
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(secretKey))
        {
            return null;
        }

        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{publicKey}:{secretKey}"));

        // Set in code, the endpoint of an HTTP exporter is used as it is (no /v1/traces appended). The headers replace
        // the Aspire dashboard API key from OTEL_EXPORTER_OTLP_HEADERS.
        return new OtlpExporterOptions
        {
            Endpoint = new Uri(baseUrl.TrimEnd('/') + "/api/public/otel/v1/traces"),
            Protocol = OtlpExportProtocol.HttpProtobuf,
            Headers = $"Authorization=Basic {auth}",
        };
    }

    /// <summary>The processor to add to the tracer provider: a batch OTLP exporter behind <see cref="AiTraceFilter"/>.</summary>
    public static BaseProcessor<Activity> CreateProcessor(OtlpExporterOptions options) =>
        new AiTraceFilter(new BatchActivityExportProcessor(new OtlpTraceExporter(options)));
}

/// <summary>
/// Passes on the spans of traces with AI work and drops the rest (health checks, static files, catalog pages), so Langfuse
/// shows LLM traces only. A span is kept when it comes from the <c>FilmSearch.AI</c> or <c>FilmSearch</c> sources, when
/// one of its in-process ancestors does (database queries of a tool), or when it ends after AI work of its trace
/// (the HTTP request span around a chat turn: it ends last, so the tree in Langfuse has its root).
/// <para>A composite processor, so the inner exporter gets the tracer provider's resource (<c>service.name</c>).</para>
/// </summary>
public sealed class AiTraceFilter(BaseProcessor<Activity> inner) : CompositeProcessor<Activity>([inner])
{
    /// <summary>Upper bound of traces remembered between their first AI span and their root span.</summary>
    internal const int MaxOpenTraces = 10_000;

    private static readonly HashSet<string> AiSources = new(StringComparer.Ordinal) { "FilmSearch.AI", "FilmSearch" };

    private readonly ConcurrentDictionary<ActivityTraceId, byte> _aiTraces = new();

    public override void OnEnd(Activity data)
    {
        var keep = IsAi(data) || _aiTraces.ContainsKey(data.TraceId);
        if (keep && data.Parent is not null)
        {
            if (_aiTraces.Count >= MaxOpenTraces)
            {
                _aiTraces.Clear();
            }

            _aiTraces.TryAdd(data.TraceId, 0);
        }

        if (data.Parent is null)
        {
            // The local root ends last: nothing more of this trace will come.
            _aiTraces.TryRemove(data.TraceId, out _);
        }

        if (keep)
        {
            base.OnEnd(data);
        }
    }

    private static bool IsAi(Activity activity)
    {
        for (var current = activity; current is not null; current = current.Parent)
        {
            if (AiSources.Contains(current.Source.Name))
            {
                return true;
            }
        }

        return false;
    }
}
