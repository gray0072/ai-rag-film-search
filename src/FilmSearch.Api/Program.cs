using System.Text.Json.Serialization;
using FilmSearch.Api.Modules.AppInfo;
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
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOwnerContext();

// Modules
builder.Services.AddAppInfoModule();

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

app.MapDefaultEndpoints();

// Modules
app.MapAppInfoEndpoints();

if (app.Configuration.GetValue<bool>(DatabaseSetup.MigrateOnStartupKey))
{
    await app.Services.MigrateDatabaseAsync();
}

await app.RunAsync();
