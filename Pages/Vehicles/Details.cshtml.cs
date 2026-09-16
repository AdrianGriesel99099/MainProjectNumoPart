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
    [Authorize]
    public class DetailsModel : PageModel
    {
        private readonly AppDbContext _db;

        public DetailsModel(AppDbContext db)
        {
            _db = db;
        }

        public Vehicle Vehicle { get; set; } = null!;
        public ILookup<Stage, Photo> PhotosByStage { get; set; } = null!;

        // Drives the coverage list: which parts have at least one photo, and how many. The count
        // is what turns it from decoration into a worklist — you can see at a glance what still
        // needs shooting before the car leaves.
        public Dictionary<Part, int> PhotoCountsByPart { get; set; } = new();

        public int UntaggedCount { get; set; }

        // Keyed by PhotoId, absent for photos with no comments — same convention as
        // PhotoCountsByPart, so it lets the tile badge below spot "has notes" without opening
        // each photo individually.
        public Dictionary<int, int> PhotoCommentCounts { get; set; } = new();

        public List<VehicleUpdate> UpdatesNewestFirst { get; set; } = new();

        public List<DamageMark> DamageMarksNewestFirst { get; set; } = new();

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var vehicle = await _db.Vehicles
                .Include(v => v.Photos)
                .Include(v => v.VehicleUpdates)
                .Include(v => v.DamageMarks).ThenInclude(m => m.Photo)
                .FirstOrDefaultAsync(v => v.Id == id);
            if (vehicle is null) return NotFound();

            Vehicle = vehicle;
            PhotosByStage = vehicle.Photos
                .OrderBy(p => p.SequenceNumber)
                .ToLookup(p => p.Stage);

            PhotoCountsByPart = vehicle.Photos
                .Where(p => p.Part.HasValue)
                .GroupBy(p => p.Part!.Value)
                .ToDictionary(g => g.Key, g => g.Count());

            UntaggedCount = vehicle.Photos.Count(p => !p.Part.HasValue);

            var photoIds = vehicle.Photos.Select(p => p.Id).ToList();
            PhotoCommentCounts = await _db.PhotoComments
                .Where(c => photoIds.Contains(c.PhotoId))
                .GroupBy(c => c.PhotoId)
                .ToDictionaryAsync(g => g.Key, g => g.Count());

            UpdatesNewestFirst = vehicle.VehicleUpdates
                .OrderByDescending(u => u.CreatedAtUtc)
                .ToList();

            DamageMarksNewestFirst = vehicle.DamageMarks
                .OrderByDescending(m => m.CreatedAtUtc)
                .ToList();

            return Page();
        }
    }
}
