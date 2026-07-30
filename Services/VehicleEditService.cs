using System.Threading;
using System.Threading.Tasks;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Models;
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

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Vehicle {VehicleId} edited by {EditorId}: Vin {OldVin}->{NewVin}, Reg {OldReg}->{NewReg}",
                vehicle.Id, editorId, previousVin, normalizedVin, previousReg, normalizedReg);

            return new VehicleEditResult(VehicleEditStatus.Updated);
        }
    }
}
