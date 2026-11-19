using Microsoft.AspNetCore.DataProtection;

namespace FilmSearch.Api.Hosting;

/// <summary>
/// Data protection keys (I20): they sign the auth cookie and the antiforgery tokens. By default they live inside the
/// container and are lost on every deploy, which signs everybody out. With <c>DataProtection:KeysPath</c> set (a volume
/// in the deployment) they survive restarts and new images.
/// </summary>
public static class DataProtectionSetup
{
    public const string KeysPathKey = "DataProtection:KeysPath";

    /// <summary>Same application name for every container and image version: cookies stay valid across deploys.</summary>
    public const string ApplicationName = "FilmSearch";

    public static WebApplicationBuilder AddDataProtectionKeys(this WebApplicationBuilder builder)
    {
        var dataProtection = builder.Services.AddDataProtection().SetApplicationName(ApplicationName);

        var keysPath = builder.Configuration[KeysPathKey];
        if (!string.IsNullOrWhiteSpace(keysPath))
        {
            // The keys are stored unencrypted: the volume is only mounted into the API container (deploy/README.md).
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        }

        return builder;
    }
}
