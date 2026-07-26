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

            if (!string.IsNullOrWhiteSpace(filter.VinOrReg))
            {
                var term = filter.VinOrReg.Trim();
                query = query.Where(p => p.Vehicle.Vin == term || p.Vehicle.Reg == term);
            }

            if (filter.UploadedFrom.HasValue)
                query = query.Where(p => p.UploadedAtUtc >= filter.UploadedFrom.Value);
            if (filter.UploadedTo.HasValue)
                query = query.Where(p => p.UploadedAtUtc <= filter.UploadedTo.Value);

            if (filter.TakenFrom.HasValue)
                query = query.Where(p => p.DateTakenUtc != null && p.DateTakenUtc >= filter.TakenFrom.Value);
            if (filter.TakenTo.HasValue)
                query = query.Where(p => p.DateTakenUtc != null && p.DateTakenUtc <= filter.TakenTo.Value);

            return query.OrderByDescending(p => p.UploadedAtUtc);
        }
    }
}
