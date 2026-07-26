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

        // This plain max+1 read is NOT safe on its own. Two concurrent requests uploading to the
        // same vehicle+stage can both run this query before either commits, and both get the same
        // "next" number — the single-instance constraint (see Global Constraints) rules out
        // multiple *replicas*, not multiple *concurrent requests* inside the one process, which
        // ASP.NET Core serves routinely. Correctness comes from the CALLER, not from here: the
        // unique index on Photo(VehicleId, Stage, SequenceNumber) turns a collision into a loud
        // SaveChangesAsync failure, and Pages/Upload.cshtml.cs catches it and retries with a
        // freshly-read number. Do not treat the value returned here as reserved.
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
