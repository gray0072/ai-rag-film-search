using FilmSearch.Worker;

// The Worker (I16): runs background jobs from the job table, the daily TMDB import (off by default) and order expiry.
// Started by the AppHost; alone: dotnet run --project src/FilmSearch.Worker (needs ConnectionStrings:filmsearch).

var builder = WorkerHost.CreateBuilder(args);
builder.AddWorkerServices();

using var host = builder.Build();
await host.MigrateOnStartupAsync();
await host.RunAsync();
