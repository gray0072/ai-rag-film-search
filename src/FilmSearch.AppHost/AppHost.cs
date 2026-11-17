var builder = DistributedApplication.CreateBuilder(args);

// PostgreSQL 17 with the pgvector extension available (ADR-0001).
// Persistent lifetime: the container keeps running between AppHost runs, so restarts are fast.
var postgres = builder.AddPostgres("postgres")
    .WithImage("pgvector/pgvector", "pg17")
    .WithContainerName("filmsearch-postgres")
    .WithDataVolume("filmsearch-postgres-data")
    .WithLifetime(ContainerLifetime.Persistent);

var database = postgres.AddDatabase("filmsearch");

builder.AddProject<Projects.FilmSearch_Api>("api")
    .WithReference(database)
    .WaitFor(database)
    .WithHttpHealthCheck("/health");

await builder.Build().RunAsync();
