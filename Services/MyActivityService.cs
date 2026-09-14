using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Models;
using Microsoft.EntityFrameworkCore;

namespace MainProjectNumoPart.Services
{
    public enum MyActivityKind
    {
        PhotoUpload,
        VehicleUpdate,
        PhotoComment,
        DamageMark
    }

    public class MyActivityItem
    {
        public MyActivityKind Kind { get; set; }
        public int VehicleId { get; set; }
        public string VehicleLabel { get; set; } = null!;
        public string Summary { get; set; } = null!;
        public DateTime TimestampUtc { get; set; }
        public int? PhotoId { get; set; }
    }

    // Backs the "what did I do today" page (Pages/MyActivity) -- a shift-end check that nothing
    // was missed, pulling together the four kinds of things a Staff/Admin user can leave behind
    // across the app (uploads, job-card updates, photo comments, damage marks) into one feed.
    // Photo uploads are grouped by vehicle+stage rather than listed one row per photo: a single
    // bulk upload can be dozens of files, and nobody reviewing their own day wants dozens of
    // identical rows for one action.
    public static class MyActivityService
    {
        public static async Task<List<MyActivityItem>> ForUserAsync(
            AppDbContext db, string userId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
        {
            var items = new List<MyActivityItem>();

            var uploadGroups = await db.Photos
                .Include(p => p.Vehicle)
                .Where(p => p.UploaderId == userId && p.UploadedAtUtc >= fromUtc && p.UploadedAtUtc < toUtc)
                .GroupBy(p => new { p.VehicleId, p.Stage })
                .Select(g => new
                {
                    g.Key.VehicleId,
                    g.Key.Stage,
                    Count = g.Count(),
                    LatestUtc = g.Max(p => p.UploadedAtUtc)
                })
                .ToListAsync(ct);
            if (uploadGroups.Count > 0)
            {
                var vehicleLabels = await VehicleLabelsAsync(db, uploadGroups.Select(g => g.VehicleId), ct);
                foreach (var g in uploadGroups)
                {
                    items.Add(new MyActivityItem
                    {
                        Kind = MyActivityKind.PhotoUpload,
                        VehicleId = g.VehicleId,
                        VehicleLabel = vehicleLabels[g.VehicleId],
                        Summary = $"Uploaded {g.Count} photo{(g.Count == 1 ? "" : "s")} ({g.Stage})",
                        TimestampUtc = g.LatestUtc
                    });
                }
            }

            var updates = await db.VehicleUpdates
                .Include(u => u.Vehicle)
                .Where(u => u.AuthorId == userId && u.CreatedAtUtc >= fromUtc && u.CreatedAtUtc < toUtc)
                .ToListAsync(ct);
            items.AddRange(updates.Select(u => new MyActivityItem
            {
                Kind = MyActivityKind.VehicleUpdate,
                VehicleId = u.VehicleId,
                VehicleLabel = VehicleLabel(u.Vehicle),
                Summary = $"Job-card update: {u.Body}",
                TimestampUtc = u.CreatedAtUtc
            }));

            var comments = await db.PhotoComments
                .Include(c => c.Photo).ThenInclude(p => p.Vehicle)
                .Where(c => c.AuthorId == userId && c.CreatedAtUtc >= fromUtc && c.CreatedAtUtc < toUtc)
                .ToListAsync(ct);
            items.AddRange(comments.Select(c => new MyActivityItem
            {
                Kind = MyActivityKind.PhotoComment,
                VehicleId = c.Photo.VehicleId,
                VehicleLabel = VehicleLabel(c.Photo.Vehicle),
                Summary = $"Comment: {c.Body}",
                TimestampUtc = c.CreatedAtUtc,
                PhotoId = c.PhotoId
            }));

            var marks = await db.DamageMarks
                .Include(m => m.Vehicle)
                .Where(m => m.AuthorId == userId && m.CreatedAtUtc >= fromUtc && m.CreatedAtUtc < toUtc)
                .ToListAsync(ct);
            items.AddRange(marks.Select(m => new MyActivityItem
            {
                Kind = MyActivityKind.DamageMark,
                VehicleId = m.VehicleId,
                VehicleLabel = VehicleLabel(m.Vehicle),
                Summary = $"Damage mark ({m.Part}): {m.Note}",
                TimestampUtc = m.CreatedAtUtc,
                PhotoId = m.PhotoId
            }));

            return items.OrderByDescending(i => i.TimestampUtc).ToList();
        }

        private static async Task<Dictionary<int, string>> VehicleLabelsAsync(
            AppDbContext db, IEnumerable<int> vehicleIds, CancellationToken ct)
        {
            var ids = vehicleIds.Distinct().ToList();
            var vehicles = await db.Vehicles.Where(v => ids.Contains(v.Id)).ToListAsync(ct);
            return vehicles.ToDictionary(v => v.Id, VehicleLabel);
        }

        private static string VehicleLabel(Vehicle vehicle) => vehicle.Reg ?? vehicle.Vin ?? $"Vehicle #{vehicle.Id}";
    }
}
