using Azure.Identity;
using Azure.Core;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Configuration;

public static class DataProtectionConfiguration
{
    public const string ApplicationName = "Atea.UnifiedWorkplace";

    public static IDataProtectionBuilder AddSharedDataProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var builder = services.AddDataProtection().SetApplicationName(ApplicationName);
        if (environment.IsDevelopment()) return builder;

        services.AddSingleton<TokenCredential>(provider =>
        {
#pragma warning disable CS0618 // Use the explicit user-assigned identity client ID; the replacement overload mishandles this Azure.Identity release.
            return new ManagedIdentityCredential(provider.GetRequiredService<IConfiguration>()["DataProtection:ManagedIdentityClientId"]!);
#pragma warning restore CS0618
        });
        services.AddHostedService<SharedDataProtectionBlobInitializer>();
        builder.PersistKeysToAzureBlobStorage(provider => new BlobClient(
            new Uri(provider.GetRequiredService<IConfiguration>()["DataProtection:BlobUri"]!, UriKind.Absolute),
            provider.GetRequiredService<TokenCredential>()));
        builder.ProtectKeysWithAzureKeyVault(
            provider => new Uri(provider.GetRequiredService<IConfiguration>()["DataProtection:KeyIdentifier"]!, UriKind.Absolute),
            provider => provider.GetRequiredService<TokenCredential>());
        return builder;
    }
}
