using MainProjectNumoPart.Data;
using MainProjectNumoPart.Services;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.IO.Compression;

namespace MainProjectNumoPart.Endpoints
{
    public static class PhotoEndpoints
    {
        private static readonly TimeSpan SasLifetime = TimeSpan.FromMinutes(15);

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
            }).RequireAuthorization(policy => policy.RequireRole("Admin"));

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
                    await memoryStream.CopyToAsync(response.Body);
                }

                return Results.Empty;
            });
        }
    }
}
