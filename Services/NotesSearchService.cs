using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MainProjectNumoPart.Data;
using Microsoft.EntityFrameworkCore;

namespace MainProjectNumoPart.Services
{
    public enum NoteSearchKind
    {
        VehicleUpdate,
        PhotoComment,
        DamageMark
    }

    // One matching entry across the three free-text logs this app keeps (job card updates,
    // photo comments, damage-mark notes). PhotoId is only set for PhotoComment — the other two
    // kinds link back to the vehicle page, not a specific photo.
    public class NoteSearchResult
    {
        public NoteSearchKind Kind { get; set; }
        public int VehicleId { get; set; }
        public string? VehicleVin { get; set; }
        public string? VehicleReg { get; set; }
        public int? PhotoId { get; set; }
        public string Snippet { get; set; } = null!;
        public string AuthorEmail { get; set; } = null!;
        public DateTime CreatedAtUtc { get; set; }
    }

    // Searches job-card updates, photo comments, and damage-mark notes for a text fragment,
    // merging all three into one newest-first feed. Mirrors VehicleLookupService's
    // ToUpper()/Contains() pattern for case-insensitive matching under the Sqlite provider.
    public class NotesSearchService
    {
        public const int MinQueryLength = 2;

        private readonly AppDbContext _db;

        public NotesSearchService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<List<NoteSearchResult>> SearchAsync(string? query, CancellationToken ct = default)
        {
            var trimmed = query?.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.Length < MinQueryLength)
            {
                return new List<NoteSearchResult>();
            }
            var fragment = trimmed.ToUpperInvariant();

            var updates = await _db.VehicleUpdates
                .Where(u => u.Body.ToUpper().Contains(fragment))
                .Select(u => new NoteSearchResult
                {
                    Kind = NoteSearchKind.VehicleUpdate,
                    VehicleId = u.VehicleId,
                    VehicleVin = u.Vehicle.Vin,
                    VehicleReg = u.Vehicle.Reg,
                    PhotoId = null,
                    Snippet = u.Body,
                    AuthorEmail = u.AuthorEmail,
                    CreatedAtUtc = u.CreatedAtUtc
                })
                .ToListAsync(ct);

            var comments = await _db.PhotoComments
                .Where(c => c.Body.ToUpper().Contains(fragment))
                .Select(c => new NoteSearchResult
                {
                    Kind = NoteSearchKind.PhotoComment,
                    VehicleId = c.Photo.VehicleId,
                    VehicleVin = c.Photo.Vehicle.Vin,
                    VehicleReg = c.Photo.Vehicle.Reg,
                    PhotoId = c.PhotoId,
                    Snippet = c.Body,
                    AuthorEmail = c.AuthorEmail,
                    CreatedAtUtc = c.CreatedAtUtc
                })
                .ToListAsync(ct);

            var damageMarks = await _db.DamageMarks
                .Where(d => d.Note.ToUpper().Contains(fragment))
                .Select(d => new NoteSearchResult
                {
                    Kind = NoteSearchKind.DamageMark,
                    VehicleId = d.VehicleId,
                    VehicleVin = d.Vehicle.Vin,
                    VehicleReg = d.Vehicle.Reg,
                    PhotoId = null,
                    Snippet = d.Note,
                    AuthorEmail = d.AuthorEmail,
                    CreatedAtUtc = d.CreatedAtUtc
                })
                .ToListAsync(ct);

            return updates.Concat(comments).Concat(damageMarks)
                .OrderByDescending(r => r.CreatedAtUtc)
                .ToList();
        }
    }
}
