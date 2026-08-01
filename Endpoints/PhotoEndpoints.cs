using MainProjectNumoPart.Authorization;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.IO.Compression;

namespace MainProjectNumoPart.Endpoints
{
    public static class PhotoEndpoints
    {
        private static readonly TimeSpan SasLifetime = TimeSpan.FromMinutes(15);

        // Part is nullable so the same endpoint clears a tag as well as setting one.
        public record SetPartRequest(int[]? Ids, Part? Part);

        public static void MapPhotoEndpoints(this WebApplication app)
        {
            var group = app.MapGroup("/api/photos").RequireAuthorization();

            group.MapGet("/{id:int}/thumbnail", async (int id, AppDbContext db, IPhotoStorage storage) =>
            {
                var photo = await db.Photos.FindAsync(id);
                if (photo is null) return Results.NotFound();
                var url = await storage.GetThumbnailReadUrlAsync(photo.BlobPathThumbnail, SasLifetime);
                return Results.Redirect(url.ToString());
            });

            group.MapGet("/{id:int}/original", async (int id, AppDbContext db, IPhotoStorage storage) =>
            {
                var photo = await db.Photos.FindAsync(id);
                if (photo is null) return Results.NotFound();
                var url = await storage.GetOriginalReadUrlAsync(photo.BlobPathOriginal, SasLifetime);
                return Results.Redirect(url.ToString());
            });

            group.MapDelete("/{id:int}", async (int id, Data.AppDbContext db, Services.IPhotoStorage storage) =>
            {
                var photo = await db.Photos.FindAsync(id);
                if (photo is null) return Results.NotFound();

                await storage.DeleteOriginalAsync(photo.BlobPathOriginal);
                await storage.DeleteThumbnailAsync(photo.BlobPathThumbnail);
                db.Photos.Remove(photo);
                await db.SaveChangesAsync();

                return Results.NoContent();
            }).RequireAuthorization(policy => policy.RequireRole(Roles.Admin));

            group.MapPost("/part", async (
                SetPartRequest? body,
                PhotoTaggingService tagging,
                UserManager<IdentityUser> userManager,
                HttpContext http,
                CancellationToken ct) =>
            {
                if (body is null) return Results.BadRequest("No photos selected.");

                var result = await tagging.SetPartAsync(
                    body.Ids ?? Array.Empty<int>(), body.Part, userManager.GetUserId(http.User)!, ct);

                return result.Status == PhotoTagStatus.Updated
                    ? Results.Ok(new { updated = result.Updated })
                    : Results.BadRequest(result.Message);
            })
            // RequireRole takes params string[] — passing the comma-joined Roles.StaffOrAdmin
            // constant here would look for one role literally named "Staff,Admin" and deny
            // everyone. That constant is only valid inside [Authorize(Roles = ...)].
            .RequireAuthorization(policy => policy.RequireRole(Roles.Staff, Roles.Admin));

            group.MapPost("/download", async (HttpRequest request, HttpResponse response, Data.AppDbContext db, Services.IPhotoStorage storage) =>
            {
                var ids = request.Form["ids"]
                    .Select(s => int.TryParse(s, out var v) ? v : (int?)null)
                    .Where(v => v.HasValue)
                    .Select(v => v!.Value)
                    .ToList();

                if (ids.Count == 0) return Results.BadRequest("No photos selected.");
                if (ids.Count > 200) return Results.BadRequest("Too many photos selected. Maximum 200 photos per download.");

                var photos = await db.Photos.Where(p => ids.Contains(p.Id)).ToListAsync();

                // The 200-ID cap above bounds the COUNT, not the bytes: at the app's 25MB per-file
                // limit, 200 photos is still ~5GB, and the whole archive is buffered in a
                // MemoryStream below (required to avoid Kestrel's sync-I/O restriction). SizeBytes
                // is already recorded per row, so the real memory cost is knowable up front —
                // reject it here rather than OOM the process mid-stream.
                var totalBytes = photos.Sum(p => p.SizeBytes);
                const long maxTotalBytes = 500L * 1024 * 1024;
                if (totalBytes > maxTotalBytes)
                {
                    return Results.BadRequest(
                        $"Selected photos total {totalBytes / (1024 * 1024)}MB, which exceeds the 500MB download limit. Select fewer photos.");
                }

                response.ContentType = "application/zip";
                response.Headers.ContentDisposition = $"attachment; filename=\"photos-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip\"";

                using (var memoryStream = new System.IO.MemoryStream())
                {
                    using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
                    {
                        foreach (var photo in photos)
                        {
                            // No compression: JPEGs/PNGs are already compressed, so re-compressing just burns CPU for no size benefit.
                            var entry = archive.CreateEntry($"{photo.Stage}/{photo.FileName}", CompressionLevel.NoCompression);
                            using var entryStream = entry.Open();
                            await using var sourceStream = await storage.OpenOriginalReadAsync(photo.BlobPathOriginal);
                            await sourceStream.CopyToAsync(entryStream);
                        }
                    }

                    memoryStream.Position = 0;
                    // The archive is fully buffered by this point, so its exact length is known —
                    // declaring it lets the browser show a real progress bar instead of falling
                    // back to chunked transfer encoding with an unknown total.
                    response.ContentLength = memoryStream.Length;
                    await memoryStream.CopyToAsync(response.Body);
                }

                return Results.Empty;
            });
        }
    }
}
