using FilmSearch.Infrastructure.Persistence;
using FilmSearch.Worker;

// The Worker (I16): runs background jobs from the job table, the daily TMDB import (off by default) and order expiry.
// Started by the AppHost; alone: dotnet run --project src/FilmSearch.Worker (needs ConnectionStrings:filmsearch).
// `migrate` (I20): applies pending migrations and exits; the deployment runs it once before the new API starts.

var builder = WorkerHost.CreateBuilder(args);
builder.AddWorkerServices();

using var host = builder.Build();
if (WorkerHost.IsMigrateCommand(args))
{
    await host.Services.MigrateDatabaseAsync();
    return;
}

await host.MigrateOnStartupAsync();
await host.RunAsync();
