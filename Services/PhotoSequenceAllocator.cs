using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Models;
using Microsoft.EntityFrameworkCore;

namespace MainProjectNumoPart.Services
{
    public class PhotoSequenceAllocator
    {
        private readonly AppDbContext _db;

        public PhotoSequenceAllocator(AppDbContext db)
        {
            _db = db;
        }

        // A plain max+1 query is safe here because the app runs as a single instance
        // (see Global Constraints) — there is no concurrent writer to race against.
        public async Task<int> NextSequenceNumberAsync(int vehicleId, Stage stage, CancellationToken ct = default)
        {
            var max = await _db.Photos
                .Where(p => p.VehicleId == vehicleId && p.Stage == stage)
                .Select(p => (int?)p.SequenceNumber)
                .MaxAsync(ct);

            return (max ?? 0) + 1;
        }
    }
}
