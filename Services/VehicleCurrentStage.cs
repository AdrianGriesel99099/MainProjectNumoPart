using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Models;
using Microsoft.EntityFrameworkCore;

namespace MainProjectNumoPart.Services
{
    // Backs the "stage" column on the home page's vehicle lists. Stage lives on Photo, not
    // Vehicle (see Models/Photo.cs), so a vehicle's current stage is taken to be the Stage of its
    // most recently uploaded photo -- the same "what did staff shoot last" signal a human glancing
    // at the vehicle's photo pile would use. Vehicles with no photos yet are absent from the
    // result, same convention as UntaggedPhotoCounts.
    public static class VehicleCurrentStage
    {
        public static async Task<Dictionary<int, Stage>> ForVehiclesAsync(
            AppDbContext db, IEnumerable<int> vehicleIds, CancellationToken ct = default)
        {
            var ids = vehicleIds.Distinct().ToList();
            if (ids.Count == 0) return new Dictionary<int, Stage>();

            var photos = await db.Photos
                .Where(p => ids.Contains(p.VehicleId))
                .Select(p => new { p.VehicleId, p.Stage, p.UploadedAtUtc, p.Id })
                .ToListAsync(ct);

            return photos
                .GroupBy(p => p.VehicleId)
                .ToDictionary(
                    g => g.Key,
                    // Id, not SequenceNumber, breaks the tie: SequenceNumber restarts at 1 for
                    // each (VehicleId, Stage) pair (see PhotoSequenceAllocator), so it only
                    // orders "shot last" correctly within a single stage. Id is a single
                    // monotonically increasing counter across every stage for the vehicle.
                    g => g.OrderByDescending(p => p.UploadedAtUtc)
                          .ThenByDescending(p => p.Id)
                          .First().Stage);
        }
    }
}
