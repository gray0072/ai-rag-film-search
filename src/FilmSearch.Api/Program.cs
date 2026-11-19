using System.Text.Json;
using System.Text.Json.Serialization;
using FilmSearch.AI.Providers;
using FilmSearch.Api.Hosting;
using FilmSearch.Api.Localization;
using FilmSearch.Api.Modules.Admin;
using FilmSearch.Api.Modules.AppInfo;
using FilmSearch.Api.Modules.Assistant;
using FilmSearch.Api.Modules.Catalog;
using FilmSearch.Api.Modules.Commerce;
using FilmSearch.Api.Modules.Identity;
using FilmSearch.Api.Modules.Ingestion;
using FilmSearch.Api.Modules.Search;
using FilmSearch.Api.OpenApi;
using FilmSearch.Api.Owners;
using FilmSearch.Api.RateLimiting;
using FilmSearch.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
if (!BuildTimeOpenApi.IsGeneratingDocument)
{
    builder.AddDatabase();
}

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddValidation();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    // Numbers are JSON numbers only (the web defaults also accept "12"): the OpenAPI document then types
    // them as `integer` / `number`, not `integer | string`, and the generated TypeScript types are plain numbers.
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});
builder.Services.AddOwnerContext();
builder.Services.AddRequestLocales();
builder.Services.AddApiRateLimiting(builder.Configuration);
builder.AddDataProtectionKeys();
builder.AddAiProviders();
builder.AddIdentityModule();

// Modules
builder.Services.AddAppInfoModule();
builder.Services.AddCatalogModule(builder.Configuration);
builder.Services.AddSearchModule(builder.Configuration);
builder.Services.AddIngestionModule(builder.Configuration);
builder.AddCommerceModule();
builder.AddAssistantModule();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseHsts();
}

// Behind a TLS-terminating proxy (the deployment's Caddy, I20) the proxy redirects HTTP to HTTPS; its health probes
// reach the API over plain HTTP and must not be redirected.
if (!app.Configuration.GetValue<bool>("FORWARDEDHEADERS_ENABLED"))
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
// After authentication: the owner is the signed-in user, else the anonymous session (I17).
app.UseOwnerContext();
app.UseAuthorization();
// After the owner context: the chat policy is per owner (I19).
app.UseRateLimiter();
app.UseRequestLocalization();

app.MapDefaultEndpoints();

// Modules
app.MapAppInfoEndpoints();
app.MapIdentityEndpoints();
app.MapCatalogEndpoints();
app.MapSearchEndpoints();
app.MapCommerceEndpoints();
app.MapAssistantEndpoints();
app.MapAdminEndpoints();

if (app.Configuration.GetValue<bool>(DatabaseSetup.MigrateOnStartupKey))
{
    await app.Services.MigrateDatabaseAsync();
}

await app.SeedIdentityAsync();
await app.SeedCatalogOnStartupAsync();
await app.QueueEmbeddingsOnStartupAsync();

await app.RunAsync();
