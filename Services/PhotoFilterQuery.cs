using System;
using System.Linq;
using MainProjectNumoPart.Models;

namespace MainProjectNumoPart.Services
{
    public class PhotoFilter
    {
        public Stage? Stage { get; set; }
        public string? VinOrReg { get; set; }
        public DateTime? UploadedFrom { get; set; }
        public DateTime? UploadedTo { get; set; }
        public DateTime? TakenFrom { get; set; }
        public DateTime? TakenTo { get; set; }
    }

    public static class PhotoFilterQuery
    {
        public static IQueryable<Photo> Apply(IQueryable<Photo> query, PhotoFilter filter)
        {
            if (filter.Stage.HasValue)
                query = query.Where(p => p.Stage == filter.Stage.Value);

            // Same normalization the lookup/create path uses, so a VIN or reg typed here in a
            // different case or with different internal spacing still matches the stored value —
            // SQLite's default collation is case-sensitive, so a plain Trim() would not.
            var term = VehicleLookupService.NormalizeIdentifier(filter.VinOrReg);
            if (term is not null)
                query = query.Where(p => p.Vehicle.Vin == term || p.Vehicle.Reg == term);

            // The *To bounds come from an HTML <input type="date">, so they always arrive as
            // midnight at the START of the chosen day. Comparing `<= bound` therefore excluded
            // everything on the end date itself — "through 1 June" silently dropped every photo
            // uploaded on 1 June. Compare against the start of the NEXT day with `<` instead, which
            // makes the range inclusive of the whole end date as a user reading the form expects.
            if (filter.UploadedFrom.HasValue)
                query = query.Where(p => p.UploadedAtUtc >= filter.UploadedFrom.Value);
            if (filter.UploadedTo.HasValue)
            {
                var uploadedToExclusive = filter.UploadedTo.Value.Date.AddDays(1);
                query = query.Where(p => p.UploadedAtUtc < uploadedToExclusive);
            }

            if (filter.TakenFrom.HasValue)
                query = query.Where(p => p.DateTakenUtc != null && p.DateTakenUtc >= filter.TakenFrom.Value);
            if (filter.TakenTo.HasValue)
            {
                var takenToExclusive = filter.TakenTo.Value.Date.AddDays(1);
                query = query.Where(p => p.DateTakenUtc != null && p.DateTakenUtc < takenToExclusive);
            }

            return query.OrderByDescending(p => p.UploadedAtUtc);
        }
    }
}
