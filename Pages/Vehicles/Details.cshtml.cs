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

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var vehicle = await _db.Vehicles.Include(v => v.Photos).FirstOrDefaultAsync(v => v.Id == id);
            if (vehicle is null) return NotFound();

            Vehicle = vehicle;
            PhotosByStage = vehicle.Photos
                .OrderBy(p => p.SequenceNumber)
                .ToLookup(p => p.Stage);

            return Page();
        }
    }
}
