using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace MainProjectNumoPart.Services
{
    public class BlobPhotoStorage : IPhotoStorage
    {
        private const string OriginalsContainer = "originals";
        private const string ThumbnailsContainer = "thumbnails";

        private readonly BlobServiceClient _serviceClient;
        private readonly BlobContainerClient _originals;
        private readonly BlobContainerClient _thumbnails;

        public BlobPhotoStorage(BlobServiceClient serviceClient)
        {
            _serviceClient = serviceClient;
            _originals = serviceClient.GetBlobContainerClient(OriginalsContainer);
            _thumbnails = serviceClient.GetBlobContainerClient(ThumbnailsContainer);
        }

        public Task UploadOriginalAsync(string blobPath, Stream content, string contentType, CancellationToken ct = default)
            => UploadAsync(_originals, blobPath, content, contentType, ct);

        public Task UploadThumbnailAsync(string blobPath, Stream content, string contentType, CancellationToken ct = default)
            => UploadAsync(_thumbnails, blobPath, content, contentType, ct);

        // Create-only, never overwrite. Photos are immutable once created: the only write path in
        // the whole app is Pages/Upload.cshtml.cs, and the only other mutation is an outright
        // delete — nothing legitimately re-uploads to a path that already holds a blob, because
        // every non-colliding upload computes a fresh (vehicle, stage, sequence, extension) path.
        //
        // IfNoneMatch = ETag.All makes the service reject the write with 409 BlobAlreadyExists if
        // anything is already at that exact path. That converts the one case where a path IS reused
        // — two concurrent uploads racing to the same vehicle+stage and landing on the same
        // sequence number — from a silent overwrite into a loud, catchable error. Without it the
        // loser's bytes could land on top of the winner's, leaving the winner's committed Photo row
        // pointing at somebody else's image: the wrong photo, with nothing to detect it. The caller
        // treats this 409 as a retryable sequence conflict (see IsSequenceConflict there).
        private static async Task UploadAsync(BlobContainerClient container, string blobPath, Stream content, string contentType, CancellationToken ct)
        {
            await container.CreateIfNotExistsAsync(cancellationToken: ct);
            var blob = container.GetBlobClient(blobPath);
            await blob.UploadAsync(content, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
                Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
            }, ct);
        }

        public async Task DeleteOriginalAsync(string blobPath, CancellationToken ct = default)
            => await _originals.GetBlobClient(blobPath).DeleteIfExistsAsync(cancellationToken: ct);

        public async Task DeleteThumbnailAsync(string blobPath, CancellationToken ct = default)
            => await _thumbnails.GetBlobClient(blobPath).DeleteIfExistsAsync(cancellationToken: ct);

        public async Task<Stream> OpenOriginalReadAsync(string blobPath, CancellationToken ct = default)
        {
            var blob = _originals.GetBlobClient(blobPath);
            var response = await blob.DownloadStreamingAsync(cancellationToken: ct);
            return response.Value.Content;
        }

        public Task<Uri> GetThumbnailReadUrlAsync(string blobPath, TimeSpan validFor, CancellationToken ct = default)
            => GetReadUrlAsync(_thumbnails, blobPath, validFor, ct);

        public Task<Uri> GetOriginalReadUrlAsync(string blobPath, TimeSpan validFor, CancellationToken ct = default)
            => GetReadUrlAsync(_originals, blobPath, validFor, ct);

        // Two auth paths, both producing a read-only, time-limited URL:
        //  - Local dev / Azurite: the connection string carries an account key, so a
        //    standard account-key SAS works directly off the BlobClient.
        //  - Production: the app authenticates via Managed Identity, which has no
        //    account key at all. A "user delegation SAS" is Azure's supported
        //    alternative — it's signed with a short-lived key obtained via Azure AD
        //    instead of the account key, so no key is ever stored anywhere.
        private async Task<Uri> GetReadUrlAsync(BlobContainerClient container, string blobPath, TimeSpan validFor, CancellationToken ct)
        {
            var blob = container.GetBlobClient(blobPath);
            var now = DateTimeOffset.UtcNow;

            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = container.Name,
                BlobName = blobPath,
                Resource = "b",
                StartsOn = now.AddMinutes(-5),
                ExpiresOn = now.Add(validFor)
            };
            sasBuilder.SetPermissions(BlobSasPermissions.Read);

            if (_serviceClient.CanGenerateAccountSasUri)
            {
                return blob.GenerateSasUri(sasBuilder);
            }

            var delegationKey = await _serviceClient.GetUserDelegationKeyAsync(now.AddMinutes(-5), now.Add(validFor), ct);
            var sasQuery = sasBuilder.ToSasQueryParameters(delegationKey.Value, _serviceClient.AccountName);

            var uriBuilder = new UriBuilder(blob.Uri) { Query = sasQuery.ToString() };
            return uriBuilder.Uri;
        }
    }
}
