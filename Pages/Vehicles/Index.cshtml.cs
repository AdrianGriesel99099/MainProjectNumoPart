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

        public List<Vehicle> Vehicles { get; set; } = new();
        public int TotalCount { get; set; }
        public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);

        // Vehicles absent from this dictionary have no untagged photos — see UntaggedPhotoCounts.
        public Dictionary<int, int> UntaggedCounts { get; set; } = new();

        public async Task OnGetAsync()
        {
            if (PageNumber < 1) PageNumber = 1;

            TotalCount = await _db.Vehicles.CountAsync();

            Vehicles = await _db.Vehicles
                .OrderByDescending(v => v.CreatedAtUtc)
                .ThenByDescending(v => v.Id)
                .Skip((PageNumber - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();

            UntaggedCounts = await Services.UntaggedPhotoCounts.ForVehiclesAsync(
                _db, Vehicles.Select(v => v.Id));
        }
    }
}
