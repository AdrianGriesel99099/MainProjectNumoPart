using System;
using System.Threading;
using System.Threading.Tasks;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MainProjectNumoPart.Services
{
    // The job-card log: free-text updates about a vehicle's job, separate from photos.
    public class VehicleUpdateService
    {
        public const int MaxBodyLength = 2000;

        private readonly AppDbContext _db;
        private readonly ILogger<VehicleUpdateService> _logger;

        public VehicleUpdateService(AppDbContext db, ILogger<VehicleUpdateService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<NoteResult> AddAsync(
            int vehicleId, string? body, string authorId, string authorEmail, CancellationToken ct = default)
        {
            var trimmed = body?.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                return new NoteResult(NoteStatus.EmptyBody, "Enter some text before posting.");
            }
            if (trimmed.Length > MaxBodyLength)
            {
                return new NoteResult(NoteStatus.TooLong, $"Updates are limited to {MaxBodyLength} characters.");
            }

            var vehicleExists = await _db.Vehicles.AnyAsync(v => v.Id == vehicleId, ct);
            if (!vehicleExists)
            {
                return new NoteResult(NoteStatus.NotFound);
            }

            _db.VehicleUpdates.Add(new VehicleUpdate
            {
                VehicleId = vehicleId,
                AuthorId = authorId,
                AuthorEmail = authorEmail,
                Body = trimmed,
                CreatedAtUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Vehicle update added on vehicle {VehicleId} by {AuthorId}", vehicleId, authorId);
            return new NoteResult(NoteStatus.Success);
        }

        public async Task<NoteResult> DeleteAsync(int updateId, CancellationToken ct = default)
        {
            var update = await _db.VehicleUpdates.FindAsync(new object[] { updateId }, ct);
            if (update is null)
            {
                return new NoteResult(NoteStatus.NotFound);
            }

            _db.VehicleUpdates.Remove(update);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Vehicle update {UpdateId} deleted", updateId);
            return new NoteResult(NoteStatus.Success);
        }
    }
}
