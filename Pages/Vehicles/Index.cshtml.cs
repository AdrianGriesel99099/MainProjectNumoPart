using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MainProjectNumoPart.Pages.Vehicles
{
    // The home page only ever shows the 10 most recently added vehicles, with no way to page
    // further back short of already knowing a VIN/Reg/make-model to search for. This is the
    // browse-everything view that fills that gap.
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly AppDbContext _db;

        public IndexModel(AppDbContext db)
        {
            _db = db;
        }

        public const int PageSize = 25;

        [BindProperty(SupportsGet = true)]
        public int PageNumber { get; set; } = 1;

        // A VIN/Reg fragment (matched the same way as the home page search) or a Make/Model
        // fragment — narrows the browse list instead of paging through everything by hand.
        [BindProperty(SupportsGet = true)]
        public string? Search { get; set; }

        // Narrows the browse list to vehicles with at least one untagged photo -- the same
        // condition that puts the warning badge in the Tagging column, surfaced as a filter so
        // staff can pull up the whole backlog instead of scanning the badge column page by page.
        [BindProperty(SupportsGet = true)]
        public bool NeedsTaggingOnly { get; set; }

        // Narrows the browse list to vehicles whose current stage (per VehicleCurrentStage, the
        // same signal behind the Stage column) matches this value -- lets staff pull up e.g.
        // every vehicle sitting at Checkout without scanning the Stage column page by page.
        [BindProperty(SupportsGet = true)]
        public Stage? StageFilter { get; set; }

        public List<Vehicle> Vehicles { get; set; } = new();
        public int TotalCount { get; set; }
        public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);

        // Vehicles absent from this dictionary have no untagged photos — see UntaggedPhotoCounts.
        public Dictionary<int, int> UntaggedCounts { get; set; } = new();

        // Vehicles absent from this dictionary have no photos yet — see VehicleCurrentStage.
        public Dictionary<int, Stage> CurrentStage { get; set; } = new();

        // Every listed vehicle has an entry — see VehicleLastActivity.
        public Dictionary<int, DateTime> LastActivity { get; set; } = new();

        public async Task OnGetAsync()
        {
            if (PageNumber < 1) PageNumber = 1;

            var query = _db.Vehicles.AsQueryable();

            var identifierFragment = Services.VehicleLookupService.NormalizeIdentifier(Search);
            if (identifierFragment is not null)
            {
                var makeModelFragment = Search!.Trim().ToUpperInvariant();
                query = query.Where(v =>
                    (v.Vin != null && v.Vin.Contains(identifierFragment)) ||
                    (v.Reg != null && v.Reg.Contains(identifierFragment)) ||
                    (v.MakeModel != null && v.MakeModel.ToUpper().Contains(makeModelFragment)));
            }

            if (NeedsTaggingOnly)
            {
                query = query.Where(v => v.Photos.Any(p => p.Part == null));
            }

            if (StageFilter.HasValue)
            {
                var stage = StageFilter.Value;
                query = query.Where(v =>
                    v.Photos.Any() &&
                    v.Photos.OrderByDescending(p => p.UploadedAtUtc)
                            .ThenByDescending(p => p.Id)
                            .First().Stage == stage);
            }

            TotalCount = await query.CountAsync();

            // Mirrors the < 1 clamp above: a page number beyond the last page (a stale bookmark,
            // a narrowed search, or a hand-edited URL) must not overshoot Skip/Take and land on an
            // empty page while TotalCount above it still reports real matches. TotalPages is 0 when
            // there are no results at all, so this only fires once there's an actual last page to
            // clamp to.
            if (TotalPages > 0 && PageNumber > TotalPages) PageNumber = TotalPages;

            Vehicles = await query
                .OrderByDescending(v => v.CreatedAtUtc)
                .ThenByDescending(v => v.Id)
                .Skip((PageNumber - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();

            UntaggedCounts = await Services.UntaggedPhotoCounts.ForVehiclesAsync(
                _db, Vehicles.Select(v => v.Id));
            CurrentStage = await Services.VehicleCurrentStage.ForVehiclesAsync(
                _db, Vehicles.Select(v => v.Id));
            LastActivity = await Services.VehicleLastActivity.ForVehiclesAsync(
                _db, Vehicles.Select(v => v.Id));
        }
    }
}
