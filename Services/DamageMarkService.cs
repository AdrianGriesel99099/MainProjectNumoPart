using System;
using System.Threading;
using System.Threading.Tasks;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MainProjectNumoPart.Services
{
    public class DamageMarkService
    {
        public const int MaxNoteLength = 2000;

        private readonly AppDbContext _db;
        private readonly ILogger<DamageMarkService> _logger;

        public DamageMarkService(AppDbContext db, ILogger<DamageMarkService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<NoteResult> AddAsync(
            int vehicleId, Part part, int? photoId, double xPercent, double yPercent, string? note,
            string authorId, string authorEmail, CancellationToken ct = default)
        {
            var trimmed = note?.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                return new NoteResult(NoteStatus.EmptyBody, "Say what the damage is before saving.");
            }
            if (trimmed.Length > MaxNoteLength)
            {
                return new NoteResult(NoteStatus.TooLong, $"Notes are limited to {MaxNoteLength} characters.");
            }

            var vehicleExists = await _db.Vehicles.AnyAsync(v => v.Id == vehicleId, ct);
            if (!vehicleExists)
            {
                return new NoteResult(NoteStatus.NotFound);
            }

            // Defends against a stale or tampered client payload — a photoId that exists but
            // belongs to a different vehicle would otherwise anchor a mark to the wrong car's
            // photo, silently.
            if (photoId.HasValue)
            {
                var photoBelongsToVehicle = await _db.Photos
                    .AnyAsync(p => p.Id == photoId.Value && p.VehicleId == vehicleId, ct);
                if (!photoBelongsToVehicle)
                {
                    return new NoteResult(NoteStatus.NotFound);
                }
            }

            _db.DamageMarks.Add(new DamageMark
            {
                VehicleId = vehicleId,
                Part = part,
                PhotoId = photoId,
                XPercent = ClampPercent(xPercent),
                YPercent = ClampPercent(yPercent),
                Note = trimmed,
                AuthorId = authorId,
                AuthorEmail = authorEmail,
                CreatedAtUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Damage mark added on vehicle {VehicleId}, part {Part}, photo {PhotoId}, by {AuthorId}",
                vehicleId, part, photoId?.ToString() ?? "(none)", authorId);

            return new NoteResult(NoteStatus.Success);
        }

        // wwwroot/css's .damage-mark-canvas-wrap clips with overflow:hidden, so a pin outside
        // 0-100 on either axis renders invisible and unclickable rather than merely off-centre --
        // the note would still save but its pin becomes unreachable. Clamped rather than rejected:
        // a legitimate click right at the image edge can overshoot slightly from floating-point
        // rect math, and that should still land the pin at the edge, not bounce the whole save.
        // NaN/Infinity can't come from that click math (only a malformed payload), so those fall
        // back to dead centre instead of clamping to an arbitrary bound.
        private static double ClampPercent(double value) =>
            double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 50;

        public async Task<NoteResult> DeleteAsync(int markId, CancellationToken ct = default)
        {
            var mark = await _db.DamageMarks.FindAsync(new object[] { markId }, ct);
            if (mark is null)
            {
                return new NoteResult(NoteStatus.NotFound);
            }

            _db.DamageMarks.Remove(mark);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Damage mark {MarkId} deleted", markId);
            return new NoteResult(NoteStatus.Success);
        }
    }
}
