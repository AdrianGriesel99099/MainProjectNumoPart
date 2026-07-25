using MainProjectNumoPart.Data;
using MainProjectNumoPart.Services;
using Microsoft.EntityFrameworkCore;

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
        }
    }
}
