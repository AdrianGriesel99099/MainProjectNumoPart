using System.Threading;
using System.Threading.Tasks;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MainProjectNumoPart.Services
{
    public enum VehicleEditStatus
    {
        Updated,
        NotFound,
        NoIdentifier,
        VinConflict,
        RegConflict
    }

    public record VehicleEditResult(VehicleEditStatus Status, string? Message = null);

    // Editing is separate from VehicleLookupService.FindOrCreateAsync on purpose. That method only
    // ever FILLS IN a missing identifier (a Reg-only car whose VIN turns up later) and never
    // overwrites one — which is correct for an upload, where the submitted text is a lookup key.
    // Here the submitted text is an explicit correction, so overwriting is the whole point, and
    // the uniqueness collisions that creates need handling that upload never has to do.
    public class VehicleEditService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<VehicleEditService> _logger;

        public VehicleEditService(AppDbContext db, ILogger<VehicleEditService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<VehicleEditResult> UpdateAsync(
            int vehicleId, string? vin, string? reg, string? makeModel, string editorId, CancellationToken ct = default)
        {
            // Same normalizer as every other identifier path, so "ab12 cde" typed here matches
            // "AB12CDE" stored by an upload — otherwise an edit could create a duplicate that
            // search would never find.
            var normalizedVin = VehicleLookupService.NormalizeIdentifier(vin);
            var normalizedReg = VehicleLookupService.NormalizeIdentifier(reg);

            if (normalizedVin is null && normalizedReg is null)
            {
                return new VehicleEditResult(VehicleEditStatus.NoIdentifier,
                    "A vehicle needs a VIN, a registration, or both — it can't have neither.");
            }

            var vehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.Id == vehicleId, ct);
            if (vehicle is null)
            {
                return new VehicleEditResult(VehicleEditStatus.NotFound);
            }

            // Checked before writing rather than relying on the unique index to throw, so the user
            // gets "that VIN belongs to another vehicle" instead of a DbUpdateException. Excludes
            // this vehicle so re-saving an unchanged form isn't a conflict with itself.
            if (normalizedVin is not null)
            {
                var clash = await _db.Vehicles
                    .AnyAsync(v => v.Vin == normalizedVin && v.Id != vehicleId, ct);
                if (clash)
                {
                    return new VehicleEditResult(VehicleEditStatus.VinConflict,
                        $"VIN '{normalizedVin}' already belongs to another vehicle.");
                }
            }

            if (normalizedReg is not null)
            {
                var clash = await _db.Vehicles
                    .AnyAsync(v => v.Reg == normalizedReg && v.Id != vehicleId, ct);
                if (clash)
                {
                    return new VehicleEditResult(VehicleEditStatus.RegConflict,
                        $"Registration '{normalizedReg}' already belongs to another vehicle.");
                }
            }

            var previousVin = vehicle.Vin;
            var previousReg = vehicle.Reg;

            vehicle.Vin = normalizedVin;
            vehicle.Reg = normalizedReg;
            vehicle.MakeModel = string.IsNullOrWhiteSpace(makeModel) ? null : makeModel.Trim();

            // BlobFolderName is deliberately NOT recomputed. Every existing photo's blob path was
            // built from it, so changing it here would orphan every image this vehicle already has
            // — the folder name is an immutable storage key, not a display value. It stops matching
            // the VIN after a correction, which is expected and harmless.

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                // The AnyAsync checks above are a separate round-trip from this save, so two
                // concurrent edits that both pass the pre-check (neither sees the other's
                // not-yet-committed VIN/Reg) can still collide here — the loser hits the unique
                // index instead of getting the friendly conflict message. Recover the same way
                // instead of letting a DbUpdateException reach the page as a 500.
                _db.ChangeTracker.Clear();

                if (normalizedVin is not null
                    && await _db.Vehicles.AnyAsync(v => v.Vin == normalizedVin && v.Id != vehicleId, ct))
                {
                    return new VehicleEditResult(VehicleEditStatus.VinConflict,
                        $"VIN '{normalizedVin}' already belongs to another vehicle.");
                }

                if (normalizedReg is not null
                    && await _db.Vehicles.AnyAsync(v => v.Reg == normalizedReg && v.Id != vehicleId, ct))
                {
                    return new VehicleEditResult(VehicleEditStatus.RegConflict,
                        $"Registration '{normalizedReg}' already belongs to another vehicle.");
                }

                throw;
            }

            _logger.LogInformation(
                "Vehicle {VehicleId} edited by {EditorId}: Vin {OldVin}->{NewVin}, Reg {OldReg}->{NewReg}",
                vehicle.Id, editorId, previousVin, normalizedVin, previousReg, normalizedReg);

            return new VehicleEditResult(VehicleEditStatus.Updated);
        }

        // Same detection VehicleLookupService.IsUniqueConstraintViolation and Upload.cshtml.cs's
        // IsSequenceConflict already need: the provider-specific exception EF Core wraps in
        // DbUpdateException differs between SQLite (dev/test) and SQL Server (production), and
        // both need recognising here or this catch would silently only work in dev.
        private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
            ex.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 }
            || ex.InnerException is SqlException { Number: 2627 or 2601 };
    }
}
