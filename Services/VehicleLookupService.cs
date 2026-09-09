using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Models;
using Microsoft.Data.Sqlite;
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

        // The single canonical form for a VIN or registration number: upper-cased and with ALL
        // whitespace removed, not merely trimmed. SQLite's default collation is case-SENSITIVE, so
        // without this "ab12cde" and "AB12CDE" compare as different vehicles — FindOrCreateAsync
        // would happily create a second Vehicle row (and therefore a second blob folder and a second
        // set of sequence counters) for the same physical car. Internal spacing matters for the same
        // reason: UK-style registrations are written both "AB12CDE" and "AB12 CDE" in the wild.
        // Every lookup, create, and filter path must run its input through THIS method so the same
        // human input normalizes identically everywhere; PhotoFilterQuery calls it too.
        // Returns null for input that is null/empty/whitespace-only, so callers can keep treating
        // "no identifier supplied" separately from "identifier that matches nothing".
        public static string? NormalizeIdentifier(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return string.Concat(value.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
        }

        // Adds (or updates) the tracked entity but does not save — the caller
        // decides when to commit, since Upload needs to add Photos in the same transaction.
        public async Task<Vehicle> FindOrCreateAsync(string? vin, string? reg, CancellationToken ct = default)
        {
            var normalizedVin = NormalizeIdentifier(vin);
            var normalizedReg = NormalizeIdentifier(reg);

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

        // Commits the Vehicle FindOrCreateAsync just resolved. FindOrCreateAsync's own lookup and
        // this save are two separate round-trips, so two concurrent uploads for the same
        // brand-new VIN/Reg can both see "nothing exists yet", both construct a new Vehicle, and
        // then race to commit it -- the loser hits the unique index on Vin/Reg (see
        // AppDbContext.OnModelCreating). That's the same class of race Upload.cshtml.cs's photo
        // sequence-number retry already guards against, just one step earlier in the same flow.
        // Recovery is a single retry, not a loop: once the winner has committed, this attempt's
        // own re-resolve is guaranteed to find that row rather than attempt another insert.
        public async Task<Vehicle> SaveWithRetryAsync(Vehicle vehicle, string? vin, string? reg, CancellationToken ct = default)
        {
            try
            {
                await _db.SaveChangesAsync(ct);
                return vehicle;
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                _db.ChangeTracker.Clear(); // discard this attempt's losing, unsaved insert
                var resolved = await FindOrCreateAsync(vin, reg, ct);
                await _db.SaveChangesAsync(ct);
                return resolved;
            }
        }

        // Same SQLite extended error code Upload.cshtml.cs's IsSequenceConflict checks for its
        // own unique-index race -- 2067 (SQLITE_CONSTRAINT_UNIQUE) specifically, not just the
        // primary code (19), which also covers foreign-key/not-null/check violations a retry
        // would not fix.
        private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
            ex.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 };

        public async Task<Vehicle?> FindBySearchTermAsync(string term, CancellationToken ct = default)
        {
            var normalized = NormalizeIdentifier(term);
            // A blank/whitespace-only term normalizes to null. Guard explicitly: letting null through
            // would compare `v.Vin == null`, which matches any Reg-only vehicle instead of nothing.
            if (normalized is null) return null;

            return await _db.Vehicles.FirstOrDefaultAsync(v => v.Vin == normalized || v.Reg == normalized, ct);
        }
    }
}
