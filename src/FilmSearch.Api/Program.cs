using System.Text.Json;
using System.Text.Json.Serialization;
using FilmSearch.Api.Localization;
using FilmSearch.Api.Modules.Admin;
using FilmSearch.Api.Modules.AppInfo;
using FilmSearch.Api.Modules.Catalog;
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

// Modules
builder.Services.AddAppInfoModule();
builder.Services.AddCatalogModule(builder.Configuration);

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
app.MapAdminEndpoints(app.Configuration);

if (app.Configuration.GetValue<bool>(DatabaseSetup.MigrateOnStartupKey))
{
    await app.Services.MigrateDatabaseAsync();
}

await app.SeedCatalogOnStartupAsync();

await app.RunAsync();
