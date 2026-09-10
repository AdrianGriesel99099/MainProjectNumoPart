using System.Linq;
using System.Threading.Tasks;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MainProjectNumoPart.Pages
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly AppDbContext _db;
        private readonly Services.VehicleLookupService _vehicles;

        public IndexModel(AppDbContext db, Services.VehicleLookupService vehicles)
        {
            _db = db;
            _vehicles = vehicles;
        }

        [BindProperty(SupportsGet = true)]
        public string? Search { get; set; }

        public string? NotFoundMessage { get; set; }
        public System.Collections.Generic.List<Vehicle> RecentVehicles { get; set; } = new();

        // Populated when a search matches no single vehicle exactly but does match several by
        // fragment or make/model — see VehicleLookupService.FindManyBySearchTermAsync.
        public System.Collections.Generic.List<Vehicle> SearchResults { get; set; } = new();

        // Vehicles absent from this dictionary have no untagged photos — see UntaggedPhotoCounts.
        public System.Collections.Generic.Dictionary<int, int> UntaggedCounts { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            if (!string.IsNullOrWhiteSpace(Search))
            {
                var vehicle = await _vehicles.FindBySearchTermAsync(Search);
                if (vehicle is not null)
                {
                    return RedirectToPage("/Vehicles/Details", new { id = vehicle.Id });
                }

                var matches = await _vehicles.FindManyBySearchTermAsync(Search);
                if (matches.Count == 1)
                {
                    return RedirectToPage("/Vehicles/Details", new { id = matches[0].Id });
                }
                if (matches.Count > 1)
                {
                    SearchResults = matches;
                }
                else
                {
                    NotFoundMessage = $"No vehicle found matching \"{Search}\".";
                }
            }

            RecentVehicles = await _db.Vehicles
                .OrderByDescending(v => v.CreatedAtUtc)
                .Take(10)
                .ToListAsync();

            var vehicleIds = RecentVehicles.Select(v => v.Id).Concat(SearchResults.Select(v => v.Id));
            UntaggedCounts = await Services.UntaggedPhotoCounts.ForVehiclesAsync(_db, vehicleIds);

            return Page();
        }
    }
}
