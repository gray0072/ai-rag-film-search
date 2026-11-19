using System.Security.Cryptography;
using Aspire.Hosting.Publishing;

/// <summary>
/// Optional self-hosted Langfuse v3 (I18, spec 10 §3): an LLM-specific trace viewer next to the Aspire dashboard. Off by
/// default; switch it on with <c>Langfuse:Enabled=true</c> in the AppHost user secrets. Six containers, as in Langfuse's
/// own docker compose: web (UI, public API and OTLP endpoint on http://localhost:3000), worker, PostgreSQL, ClickHouse,
/// Redis and MinIO (event blobs). Data lives in named volumes; all secrets are generated once and kept in the AppHost user
/// secrets. Headless init creates the organization, the project with its API keys and the admin login.
/// </summary>
internal static class Langfuse
{
    /// <summary>Host port of the Langfuse UI (NEXTAUTH_URL must be the URL the browser uses).</summary>
    public const int WebPort = 3000;

    private const string WebUrl = "http://localhost:3000";

    public const string AdminEmail = "admin@filmsearch.local";

    /// <summary>Image tags: Langfuse major version 3 (its own compose file uses this tag), the others pinned.</summary>
    private const string LangfuseTag = "3";

    public static LangfuseResources AddLangfuse(this IDistributedApplicationBuilder builder)
    {
        var postgresPassword = Secret(builder, "langfuse-postgres-password");
        var clickhousePassword = Secret(builder, "langfuse-clickhouse-password");
        var redisPassword = Secret(builder, "langfuse-redis-password");
        var minioPassword = Secret(builder, "langfuse-minio-password");
        var nextAuthSecret = Secret(builder, "langfuse-nextauth-secret");
        var salt = Secret(builder, "langfuse-salt");
        var encryptionKey = builder.AddParameter("langfuse-encryption-key", new HexParameterDefault(32), secret: true, persist: true);
        var publicKey = Secret(builder, "langfuse-public-key");
        var secretKey = Secret(builder, "langfuse-secret-key");
        var adminPassword = Secret(builder, "langfuse-admin-password");

        // Same image as the app database: no extra download.
        var postgres = builder.AddContainer("langfuse-postgres", "pgvector/pgvector", "pg17")
            .WithEnvironment("POSTGRES_USER", "postgres")
            .WithEnvironment("POSTGRES_PASSWORD", postgresPassword)
            .WithEnvironment("POSTGRES_DB", "postgres")
            .WithVolume("filmsearch-langfuse-postgres", "/var/lib/postgresql/data");

        var clickhouse = builder.AddContainer("langfuse-clickhouse", "clickhouse/clickhouse-server", "25.8")
            .WithEnvironment("CLICKHOUSE_DB", "default")
            .WithEnvironment("CLICKHOUSE_USER", "clickhouse")
            .WithEnvironment("CLICKHOUSE_PASSWORD", clickhousePassword)
            .WithVolume("filmsearch-langfuse-clickhouse", "/var/lib/clickhouse")
            .WithHttpEndpoint(targetPort: 8123, name: "http")
            .WithHttpHealthCheck("/ping", endpointName: "http");

        var redis = builder.AddContainer("langfuse-redis", "redis", "7")
            .WithArgs("--requirepass", redisPassword, "--maxmemory-policy", "noeviction");

        // The image has no shell init; the bucket is a directory that must exist before MinIO starts.
        var minio = builder.AddContainer("langfuse-minio", "cgr.dev/chainguard/minio", "latest")
            .WithEntrypoint("sh")
            .WithArgs("-c", "mkdir -p /data/langfuse && minio server --address :9000 --console-address :9001 /data")
            .WithEnvironment("MINIO_ROOT_USER", "minio")
            .WithEnvironment("MINIO_ROOT_PASSWORD", minioPassword)
            .WithVolume("filmsearch-langfuse-minio", "/data")
            .WithHttpEndpoint(targetPort: 9000, name: "http")
            .WithHttpHealthCheck("/minio/health/live", endpointName: "http");

        // Containers reach each other by resource name on the Aspire container network.
        void Common(IResourceBuilder<ContainerResource> container) => container
            .WithEnvironment("DATABASE_URL", ReferenceExpression.Create($"postgresql://postgres:{postgresPassword}@langfuse-postgres:5432/postgres"))
            .WithEnvironment("SALT", salt)
            .WithEnvironment("ENCRYPTION_KEY", encryptionKey)
            .WithEnvironment("TELEMETRY_ENABLED", "false")
            .WithEnvironment("CLICKHOUSE_MIGRATION_URL", "clickhouse://langfuse-clickhouse:9000")
            .WithEnvironment("CLICKHOUSE_URL", "http://langfuse-clickhouse:8123")
            .WithEnvironment("CLICKHOUSE_USER", "clickhouse")
            .WithEnvironment("CLICKHOUSE_PASSWORD", clickhousePassword)
            .WithEnvironment("CLICKHOUSE_CLUSTER_ENABLED", "false")
            .WithEnvironment("REDIS_HOST", "langfuse-redis")
            .WithEnvironment("REDIS_PORT", "6379")
            .WithEnvironment("REDIS_AUTH", redisPassword)
            .WithEnvironment("LANGFUSE_S3_EVENT_UPLOAD_BUCKET", "langfuse")
            .WithEnvironment("LANGFUSE_S3_EVENT_UPLOAD_REGION", "auto")
            .WithEnvironment("LANGFUSE_S3_EVENT_UPLOAD_ACCESS_KEY_ID", "minio")
            .WithEnvironment("LANGFUSE_S3_EVENT_UPLOAD_SECRET_ACCESS_KEY", minioPassword)
            .WithEnvironment("LANGFUSE_S3_EVENT_UPLOAD_ENDPOINT", "http://langfuse-minio:9000")
            .WithEnvironment("LANGFUSE_S3_EVENT_UPLOAD_FORCE_PATH_STYLE", "true")
            .WithEnvironment("LANGFUSE_S3_EVENT_UPLOAD_PREFIX", "events/")
            .WaitFor(postgres)
            .WaitFor(clickhouse)
            .WaitFor(redis)
            .WaitFor(minio);

        var worker = builder.AddContainer("langfuse-worker", "langfuse/langfuse-worker", LangfuseTag);
        Common(worker);

        var web = builder.AddContainer("langfuse", "langfuse/langfuse", LangfuseTag)
            .WithHttpEndpoint(port: WebPort, targetPort: 3000, name: "http")
            .WithExternalHttpEndpoints()
            .WithHttpHealthCheck("/api/public/health", endpointName: "http")
            .WithEnvironment("NEXTAUTH_URL", WebUrl)
            .WithEnvironment("NEXTAUTH_SECRET", nextAuthSecret)
            .WithEnvironment("LANGFUSE_INIT_ORG_ID", "filmsearch")
            .WithEnvironment("LANGFUSE_INIT_ORG_NAME", "FilmSearch")
            .WithEnvironment("LANGFUSE_INIT_PROJECT_ID", "filmsearch")
            .WithEnvironment("LANGFUSE_INIT_PROJECT_NAME", "FilmSearch")
            .WithEnvironment("LANGFUSE_INIT_PROJECT_PUBLIC_KEY", ReferenceExpression.Create($"pk-lf-{publicKey}"))
            .WithEnvironment("LANGFUSE_INIT_PROJECT_SECRET_KEY", ReferenceExpression.Create($"sk-lf-{secretKey}"))
            .WithEnvironment("LANGFUSE_INIT_USER_EMAIL", AdminEmail)
            .WithEnvironment("LANGFUSE_INIT_USER_NAME", "Admin")
            .WithEnvironment("LANGFUSE_INIT_USER_PASSWORD", adminPassword);
        Common(web);

        return new LangfuseResources(
            web.GetEndpoint("http"),
            ReferenceExpression.Create($"pk-lf-{publicKey}"),
            ReferenceExpression.Create($"sk-lf-{secretKey}"));
    }

    /// <summary>Sends traces (OTLP) and feedback scores of a service to Langfuse (read by ServiceDefaults and the Langfuse options).</summary>
    public static IResourceBuilder<T> WithLangfuse<T>(this IResourceBuilder<T> resource, LangfuseResources langfuse)
        where T : IResourceWithEnvironment => resource
        .WithEnvironment("Langfuse__BaseUrl", langfuse.Endpoint)
        .WithEnvironment("Langfuse__PublicKey", langfuse.PublicKey)
        .WithEnvironment("Langfuse__SecretKey", langfuse.SecretKey);

    /// <summary>A generated secret of letters and digits (safe in URLs and keys), kept in the AppHost user secrets.</summary>
    private static IResourceBuilder<ParameterResource> Secret(IDistributedApplicationBuilder builder, string name) =>
        builder.AddParameter(name, new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true, persist: true);

    /// <summary>A random hex string (Langfuse ENCRYPTION_KEY: 256 bits as 64 hex characters).</summary>
    private sealed class HexParameterDefault(int bytes) : ParameterDefault
    {
        public override string GetDefaultValue() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(bytes));

        public override void WriteToManifest(ManifestPublishingContext context) =>
            throw new NotSupportedException("Langfuse in the AppHost is for local development only.");
    }
}

internal sealed record LangfuseResources(EndpointReference Endpoint, ReferenceExpression PublicKey, ReferenceExpression SecretKey);
