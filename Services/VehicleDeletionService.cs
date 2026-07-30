using System.Threading;
using System.Threading.Tasks;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MainProjectNumoPart.Services
{
    public enum VehicleDeleteStatus
    {
        Deleted,
        NotFound,
        ConfirmationMismatch
    }

    public record VehicleDeleteResult(VehicleDeleteStatus Status, int PhotosDeleted = 0, string? Message = null);

    // The whole delete operation lives here rather than in the endpoint so the confirmation check
    // and the blob cleanup are unit-testable (VehicleDeletionServiceTests uses FakePhotoStorage).
    // The typed-confirmation check in particular must be verifiable: it is a server-side control,
    // and the browser's confirm() dialog is not.
    public class VehicleDeletionService
    {
        private readonly AppDbContext _db;
        private readonly IPhotoStorage _storage;
        private readonly ILogger<VehicleDeletionService> _logger;

        public VehicleDeletionService(AppDbContext db, IPhotoStorage storage, ILogger<VehicleDeletionService> logger)
        {
            _db = db;
            _storage = storage;
            _logger = logger;
        }

        public async Task<VehicleDeleteResult> DeleteAsync(int vehicleId, string? confirmation, CancellationToken ct = default)
        {
            // Photos are Included because we need every blob path before anything is removed —
            // and, usefully, because loading them makes EF issue the child DELETEs itself, so
            // correctness no longer rests on the database's ON DELETE CASCADE (present in both
            // providers' migrations, now just a backstop).
            var vehicle = await _db.Vehicles
                .Include(v => v.Photos)
                .FirstOrDefaultAsync(v => v.Id == vehicleId, ct);

            if (vehicle is null)
            {
                return new VehicleDeleteResult(VehicleDeleteStatus.NotFound);
            }

            if (!ConfirmationMatches(vehicle, confirmation))
            {
                return new VehicleDeleteResult(VehicleDeleteStatus.ConfirmationMismatch, 0,
                    "The confirmation text doesn't match this vehicle's VIN or registration.");
            }

            // Blobs BEFORE the row, deliberately. A partial failure this way leaves the Vehicle and
            // Photo rows intact pointing at some already-deleted blobs: broken thumbnails, and
            // re-running the delete finishes the job (DeleteIfExistsAsync is idempotent, so the
            // already-gone ones no-op). The reverse order is unrecoverable — once the rows are gone
            // the surviving blobs' paths are recorded nowhere and they cost money forever. This
            // also matches the single-photo delete in PhotoEndpoints.
            //
            // Sequential on purpose: a vehicle with 200 photos means 400 round trips, which is fine
            // at a workshop's realistic photo counts. If that ever becomes slow enough to brush the
            // ingress timeout, chunk it with Task.WhenAll — BlobServiceClient is thread-safe.
            foreach (var photo in vehicle.Photos)
            {
                await _storage.DeleteOriginalAsync(photo.BlobPathOriginal, ct);
                await _storage.DeleteThumbnailAsync(photo.BlobPathThumbnail, ct);
            }

            var photoCount = vehicle.Photos.Count;

            _db.Vehicles.Remove(vehicle);
            await _db.SaveChangesAsync(ct);

            // A hard delete destroys the UploaderId attribution trail on every photo. This line is
            // the only lasting record that the vehicle ever existed.
            _logger.LogInformation(
                "Vehicle deleted: Id={VehicleId} Vin={Vin} Reg={Reg} Photos={PhotoCount}",
                vehicle.Id, vehicle.Vin, vehicle.Reg, photoCount);

            return new VehicleDeleteResult(VehicleDeleteStatus.Deleted, photoCount);
        }

        private static bool ConfirmationMatches(Vehicle vehicle, string? confirmation)
        {
            // Reuse the one canonical normalizer. Stored Vin/Reg are already upper-cased with all
            // whitespace stripped, so without this "ab12 cde" would fail to confirm "AB12CDE" and
            // the check would be unusable in practice.
            var typed = VehicleLookupService.NormalizeIdentifier(confirmation);
            if (typed is null) return false;

            // Accepts EITHER identifier when the vehicle has both. Most do, and Reg is what staff
            // say out loud — rejecting a correctly-typed Reg because a VIN also exists would be
            // pure friction. This is an attention check, not a secret: the VIN is visible through
            // the windscreen.
            return (vehicle.Vin is not null && typed == vehicle.Vin)
                || (vehicle.Reg is not null && typed == vehicle.Reg);
        }
    }
}
