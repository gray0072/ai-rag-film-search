using System.Text.Json;
using System.Text.Json.Serialization;
using FilmSearch.AI.Providers;
using FilmSearch.Api.Localization;
using FilmSearch.Api.Modules.Admin;
using FilmSearch.Api.Modules.AppInfo;
using FilmSearch.Api.Modules.Catalog;
using FilmSearch.Api.Modules.Commerce;
using FilmSearch.Api.Modules.Search;
using FilmSearch.Api.OpenApi;
using FilmSearch.Api.Owners;
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
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddOwnerContext();
builder.Services.AddRequestLocales();
builder.AddAiProviders();

// Modules
builder.Services.AddAppInfoModule();
builder.Services.AddCatalogModule(builder.Configuration);
builder.Services.AddSearchModule(builder.Configuration);
builder.AddCommerceModule();

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

app.UseHttpsRedirection();
app.UseOwnerContext();
app.UseRequestLocalization();

app.MapDefaultEndpoints();

// Modules
app.MapAppInfoEndpoints();
app.MapCatalogEndpoints();
app.MapSearchEndpoints();
app.MapCommerceEndpoints();
app.MapAdminEndpoints(app.Configuration);

if (app.Configuration.GetValue<bool>(DatabaseSetup.MigrateOnStartupKey))
{
    await app.Services.MigrateDatabaseAsync();
}

await app.SeedCatalogOnStartupAsync();
app.IndexEmbeddingsOnStartup();

await app.RunAsync();
