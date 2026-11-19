var builder = DistributedApplication.CreateBuilder(args);

// PostgreSQL 17 with the pgvector extension available (ADR-0001).
// Persistent lifetime: the container keeps running between AppHost runs, so restarts are fast.
var postgres = builder.AddPostgres("postgres")
    .WithImage("pgvector/pgvector", "pg17")
    .WithContainerName("filmsearch-postgres")
    .WithDataVolume("filmsearch-postgres-data")
    .WithLifetime(ContainerLifetime.Persistent);

var database = postgres.AddDatabase("filmsearch");

var api = builder.AddProject<Projects.FilmSearch_Api>("api")
    .WithReference(database)
    .WaitFor(database)
    .WithHttpHealthCheck("/health");

// Background jobs (I16): TMDB import, embedding reindex, order expiry. It applies migrations itself in Development
// (advisory lock), so it does not wait for the API; jobs queued by the API are picked up from the job table.
var worker = builder.AddProject<Projects.FilmSearch_Worker>("worker")
    .WithReference(database)
    .WaitFor(database);

// React app (Vite dev server). `npm install` runs before the start; the API URL is injected
// as service discovery variables and the Vite dev server proxies /api to it (src/web/vite.config.ts).
var web = builder.AddViteApp("web", "../web")
    .WithNpm(install: true)
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

// Stripe (I15) sends the browser back to absolute URLs: the API resolves its success / cancel paths against the web app.
// The Worker gets it too: it runs the same Commerce options validation (Stripe needs absolute URLs).
api.WithEnvironment("Commerce__WebBaseUrl", web.GetEndpoint("http"));
worker.WithEnvironment("Commerce__WebBaseUrl", web.GetEndpoint("http"));

// Optional LLM trace viewer (I18): `dotnet user-secrets set Langfuse:Enabled true --project src/FilmSearch.AppHost`.
// The API sends its AI traces and the users' answer ratings there; the Aspire dashboard keeps getting everything.
if (bool.TryParse(builder.Configuration["Langfuse:Enabled"], out var langfuseEnabled) && langfuseEnabled)
{
    var langfuse = builder.AddLangfuse();
    api.WithLangfuse(langfuse);
}

await builder.Build().RunAsync();
