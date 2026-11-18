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

// React app (Vite dev server). `npm install` runs before the start; the API URL is injected
// as service discovery variables and the Vite dev server proxies /api to it (src/web/vite.config.ts).
var web = builder.AddViteApp("web", "../web")
    .WithNpm(install: true)
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

// Stripe (I15) sends the browser back to absolute URLs: the API resolves its success / cancel paths against the web app.
api.WithEnvironment("Commerce__WebBaseUrl", web.GetEndpoint("http"));

await builder.Build().RunAsync();
