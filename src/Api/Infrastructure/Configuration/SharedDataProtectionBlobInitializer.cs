using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Hosting;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Configuration;

public sealed class SharedDataProtectionBlobInitializer(
    IConfiguration configuration,
    TokenCredential credential,
    ILogger<SharedDataProtectionBlobInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var blob = new BlobClient(new Uri(configuration["DataProtection:BlobUri"]!, UriKind.Absolute), credential);
        if ((await blob.ExistsAsync(cancellationToken)).Value) return;

        try
        {
            await blob.UploadAsync(BinaryData.FromString("<repository />"), overwrite: false, cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status is 409 or 412)
        {
            // Another replica initialized the same shared key-ring blob first.
        }
        logger.LogInformation("Shared Data Protection key storage is ready.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
