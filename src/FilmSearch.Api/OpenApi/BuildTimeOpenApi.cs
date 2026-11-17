using System.Reflection;

namespace FilmSearch.Api.OpenApi;

/// <summary>
/// The OpenAPI document is generated on build (Microsoft.Extensions.ApiDescription.Server): the tool starts
/// Program.cs up to <c>builder.Build()</c> inside the <c>GetDocument.Insider</c> process to read endpoint metadata.
/// Infrastructure that needs configuration (the database) is skipped in that mode.
/// </summary>
public static class BuildTimeOpenApi
{
    public static bool IsGeneratingDocument { get; } =
        Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";
}
