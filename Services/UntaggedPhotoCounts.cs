using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MainProjectNumoPart.Data;
using Microsoft.EntityFrameworkCore;

namespace MainProjectNumoPart.Services
{
    // Backs the "still needs tagging" badge on the home page's vehicle lists — a quick worklist
    // signal without opening each vehicle. Vehicles with zero untagged photos are simply absent
    // from the result rather than present with a 0, so callers can test presence instead of value.
    public static class UntaggedPhotoCounts
    {
        public static async Task<Dictionary<int, int>> ForVehiclesAsync(
            AppDbContext db, IEnumerable<int> vehicleIds, CancellationToken ct = default)
        {
            var ids = vehicleIds.Distinct().ToList();
            if (ids.Count == 0) return new Dictionary<int, int>();

            return await db.Photos
                .Where(p => ids.Contains(p.VehicleId) && p.Part == null)
                .GroupBy(p => p.VehicleId)
                .Select(g => new { VehicleId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.VehicleId, x => x.Count, ct);
        }
    }
}
