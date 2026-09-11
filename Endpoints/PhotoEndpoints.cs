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

        public record AddCommentRequest(string? Body);

        public record ExportPhotosRequest(int[]? Ids, Services.PhotoExportFormat? Format, bool IncludeDamageMarks);

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

                // DamageMark.Photo is Restrict, not Cascade, at the DB level (a second DB-level
                // cascade path into DamageMarks alongside Vehicle->DamageMark is what SQL Server
                // rejects outright — see the comment in AppDbContext), so a photo-anchored mark's
                // cleanup happens here in application code instead.
                var marks = await db.DamageMarks.Where(m => m.PhotoId == id).ToListAsync();
                db.DamageMarks.RemoveRange(marks);
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

            group.MapPost("/export", async (
                ExportPhotosRequest? body, HttpResponse response, Services.PhotoExportService exporter, CancellationToken ct) =>
            {
                if (body?.Ids is null || body.Ids.Length == 0) return Results.BadRequest("No photos selected.");

                var result = await exporter.ExportAsync(
                    body.Ids, body.Format ?? Services.PhotoExportFormat.Pdf, body.IncludeDamageMarks, ct);

                if (result.Status != Services.PhotoExportStatus.Success)
                {
                    return Results.BadRequest(result.Message ?? "Could not export the selected photos.");
                }

                response.ContentType = result.ContentType!;
                response.Headers.ContentDisposition = $"attachment; filename=\"{result.FileName}\"";
                return Results.Bytes(result.Content!, result.ContentType);
            });

            group.MapPost("/{id:int}/comments", async (
                int id,
                AddCommentRequest? body,
                PhotoCommentService comments,
                UserManager<IdentityUser> userManager,
                HttpContext http,
                CancellationToken ct) =>
            {
                var userId = userManager.GetUserId(http.User)!;
                var email = (await userManager.FindByIdAsync(userId))?.Email ?? userId;

                var result = await comments.AddAsync(id, body?.Body, userId, email, ct);

                return result.Status switch
                {
                    NoteStatus.Success => Results.Ok(),
                    NoteStatus.NotFound => Results.NotFound(),
                    _ => Results.BadRequest(result.Message)
                };
            })
            // Two arguments, never the comma-joined Roles.StaffOrAdmin constant — see the
            // tagging endpoint above for why that silently denies everyone.
            .RequireAuthorization(policy => policy.RequireRole(Roles.Staff, Roles.Admin));

            group.MapDelete("/comments/{commentId:int}", async (
                int commentId,
                PhotoCommentService comments,
                CancellationToken ct) =>
            {
                var result = await comments.DeleteAsync(commentId, ct);
                return result.Status == NoteStatus.Success ? Results.NoContent() : Results.NotFound();
            }).RequireAuthorization(policy => policy.RequireRole(Roles.Admin));
        }
    }
}
