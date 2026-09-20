using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MainProjectNumoPart.Data;
using Microsoft.EntityFrameworkCore;

namespace MainProjectNumoPart.Services
{
    // Backs the "Last activity" column on the All Vehicles page. Unlike VehicleCurrentStage
    // (which only knows about photos and omits photo-less vehicles), this looks across every
    // kind of thing that can happen to a vehicle -- a photo upload, a job-card update, or a
    // damage mark -- and always has an answer, falling back to Vehicle.CreatedAtUtc for a
    // vehicle nothing has happened to yet.
    public static class VehicleLastActivity
    {
        public static readonly TimeSpan StaleThreshold = TimeSpan.FromDays(7);

        public static async Task<Dictionary<int, DateTime>> ForVehiclesAsync(
            AppDbContext db, IEnumerable<int> vehicleIds, CancellationToken ct = default)
        {
            var ids = vehicleIds.Distinct().ToList();
            if (ids.Count == 0) return new Dictionary<int, DateTime>();

            var vehicles = await db.Vehicles
                .Where(v => ids.Contains(v.Id))
                .Select(v => new { v.Id, v.CreatedAtUtc })
                .ToListAsync(ct);

            var photoTimes = await db.Photos
                .Where(p => ids.Contains(p.VehicleId))
                .GroupBy(p => p.VehicleId)
                .Select(g => new { VehicleId = g.Key, LatestUtc = g.Max(p => p.UploadedAtUtc) })
                .ToListAsync(ct);

            var updateTimes = await db.VehicleUpdates
                .Where(u => ids.Contains(u.VehicleId))
                .GroupBy(u => u.VehicleId)
                .Select(g => new { VehicleId = g.Key, LatestUtc = g.Max(u => u.CreatedAtUtc) })
                .ToListAsync(ct);

            var damageMarkTimes = await db.DamageMarks
                .Where(d => ids.Contains(d.VehicleId))
                .GroupBy(d => d.VehicleId)
                .Select(g => new { VehicleId = g.Key, LatestUtc = g.Max(d => d.CreatedAtUtc) })
                .ToListAsync(ct);

            var result = vehicles.ToDictionary(v => v.Id, v => v.CreatedAtUtc);

            foreach (var group in new[] { photoTimes, updateTimes, damageMarkTimes })
            {
                foreach (var entry in group)
                {
                    if (!result.TryGetValue(entry.VehicleId, out var current) || entry.LatestUtc > current)
                    {
                        result[entry.VehicleId] = entry.LatestUtc;
                    }
                }
            }

            return result;
        }

        public static bool IsStale(DateTime lastActivityUtc, DateTime nowUtc) =>
            nowUtc - lastActivityUtc > StaleThreshold;
    }
}
