using System;
using System.Threading;
using System.Threading.Tasks;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Models;
using Microsoft.EntityFrameworkCore;

namespace MainProjectNumoPart.Services
{
    // Thrown by FindOrCreateAsync when the VIN and Reg supplied in one upload resolve to two
    // different existing vehicles — almost always a typo in one of the two fields. Callers
    // (Task 10's Upload handler) must catch this specifically and surface it as a validation
    // error rather than letting it become an unhandled exception.
    public class VehicleIdentifierConflictException : Exception
    {
        public VehicleIdentifierConflictException(string message) : base(message)
        {
        }
    }

    public class VehicleLookupService
    {
        private readonly AppDbContext _db;

        public VehicleLookupService(AppDbContext db)
        {
            _db = db;
        }

        // Adds (or updates) the tracked entity but does not save — the caller
        // decides when to commit, since Upload needs to add Photos in the same transaction.
        public async Task<Vehicle> FindOrCreateAsync(string? vin, string? reg, CancellationToken ct = default)
        {
            var normalizedVin = string.IsNullOrWhiteSpace(vin) ? null : vin.Trim();
            var normalizedReg = string.IsNullOrWhiteSpace(reg) ? null : reg.Trim();

            if (normalizedVin is null && normalizedReg is null)
                throw new ArgumentException("At least one of VIN or Reg is required.");

            // Looked up separately (not a single OR query) specifically so a VIN match and a
            // Reg match that resolve to two DIFFERENT vehicles can be detected and rejected,
            // rather than silently picking one of the two.
            var byVin = normalizedVin is not null
                ? await _db.Vehicles.FirstOrDefaultAsync(v => v.Vin == normalizedVin, ct)
                : null;
            var byReg = normalizedReg is not null
                ? await _db.Vehicles.FirstOrDefaultAsync(v => v.Reg == normalizedReg, ct)
                : null;

            if (byVin is not null && byReg is not null && byVin.Id != byReg.Id)
            {
                throw new VehicleIdentifierConflictException(
                    $"VIN '{normalizedVin}' belongs to a different vehicle than Reg '{normalizedReg}'. Check for a typo before uploading.");
            }

            var existing = byVin ?? byReg;

            if (existing is not null)
            {
                // A car logged Reg-only can have its VIN discovered later.
                // BlobFolderName is never touched — that's what keeps existing blobs valid.
                if (normalizedVin is not null && existing.Vin is null) existing.Vin = normalizedVin;
                if (normalizedReg is not null && existing.Reg is null) existing.Reg = normalizedReg;
                return existing;
            }

            var vehicle = new Vehicle
            {
                Vin = normalizedVin,
                Reg = normalizedReg,
                BlobFolderName = PhotoNaming.ResolveBlobFolderName(normalizedVin, normalizedReg),
                CreatedAtUtc = DateTime.UtcNow
            };

            _db.Vehicles.Add(vehicle);
            return vehicle;
        }

        public async Task<Vehicle?> FindBySearchTermAsync(string term, CancellationToken ct = default)
        {
            var normalized = term.Trim();
            return await _db.Vehicles.FirstOrDefaultAsync(v => v.Vin == normalized || v.Reg == normalized, ct);
        }
    }
}
