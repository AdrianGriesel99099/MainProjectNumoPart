using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MainProjectNumoPart.Pages.Photos
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly AppDbContext _db;

        public IndexModel(AppDbContext db)
        {
            _db = db;
        }

        [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)] public Stage? Stage { get; set; }

        // One control in the UI ("All parts" / "Untagged" / each part), but Untagged is not a
        // Part value — it means Part IS NULL, which needs its own bool on PhotoFilter. Binding
        // a single string here and splitting it in OnGetAsync keeps that translation in one
        // place instead of leaking a magic "untagged" enum value into the filter/query layer.
        [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)] public string? PartFilter { get; set; }

        [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)] public string? VinOrReg { get; set; }
        [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)] public DateTime? UploadedFrom { get; set; }
        [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)] public DateTime? UploadedTo { get; set; }
        [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)] public DateTime? TakenFrom { get; set; }
        [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)] public DateTime? TakenTo { get; set; }

        public const int DisplayCap = 120;

        public List<Photo> Photos { get; set; } = new();
        public int TotalMatchCount { get; set; }
        public bool IsTruncated => TotalMatchCount > Photos.Count;

        // Populated when a from/to pair is the wrong way round (a swapped-date typo). The filter
        // still runs as entered rather than silently correcting it, so this is what tells the user
        // an empty grid means "fix your dates," not "this vehicle really has nothing here."
        public List<string> DateRangeWarnings { get; } = new();

        public bool HasActiveFilters =>
            Stage is not null || !string.IsNullOrEmpty(PartFilter) || !string.IsNullOrEmpty(VinOrReg) ||
            UploadedFrom is not null || UploadedTo is not null || TakenFrom is not null || TakenTo is not null;

        public async Task OnGetAsync()
        {
            var untagged = PartFilter == "untagged";
            Part? part = !untagged && Enum.TryParse<Part>(PartFilter, out var parsed) ? parsed : null;

            if (UploadedFrom.HasValue && UploadedTo.HasValue && UploadedFrom > UploadedTo)
            {
                DateRangeWarnings.Add("The \"Uploaded\" date range is invalid: the first date is after the second. No photos can match until this is fixed.");
            }
            if (TakenFrom.HasValue && TakenTo.HasValue && TakenFrom > TakenTo)
            {
                DateRangeWarnings.Add("The \"Taken\" date range is invalid: the first date is after the second. No photos can match until this is fixed.");
            }

            var filter = new PhotoFilter
            {
                Stage = Stage,
                Part = part,
                UntaggedOnly = untagged,
                VinOrReg = VinOrReg,
                UploadedFrom = UploadedFrom,
                UploadedTo = UploadedTo,
                TakenFrom = TakenFrom,
                TakenTo = TakenTo
            };

            var matching = PhotoFilterQuery.Apply(_db.Photos.Include(p => p.Vehicle), filter);

            TotalMatchCount = await matching.CountAsync();
            Photos = await matching.Take(DisplayCap).ToListAsync();
        }
    }
}
