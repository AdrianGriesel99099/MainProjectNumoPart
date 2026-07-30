// Pages/Vehicles/Edit.cshtml.cs
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using MainProjectNumoPart.Authorization;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MainProjectNumoPart.Pages.Vehicles
{
    // Staff and Admin, matching Upload: correcting a mistyped plate or filling in a make/model is
    // routine data entry done by the same people photographing the car. Deletion stays admin-only.
    [Authorize(Roles = Roles.StaffOrAdmin)]
    public class EditModel : PageModel
    {
        private readonly AppDbContext _db;
        private readonly VehicleEditService _editor;
        private readonly UserManager<IdentityUser> _userManager;

        public EditModel(AppDbContext db, VehicleEditService editor, UserManager<IdentityUser> userManager)
        {
            _db = db;
            _editor = editor;
            _userManager = userManager;
        }

        [BindProperty(SupportsGet = true)]
        public int Id { get; set; }

        [BindProperty] public InputModel Input { get; set; } = new();

        public int PhotoCount { get; set; }
        public string? ErrorMessage { get; set; }

        public class InputModel
        {
            public string? Vin { get; set; }
            public string? Reg { get; set; }

            [StringLength(100)]
            [Display(Name = "Make and model")]
            public string? MakeModel { get; set; }
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var vehicle = await _db.Vehicles
                .AsNoTracking()
                .Include(v => v.Photos)
                .FirstOrDefaultAsync(v => v.Id == Id);

            if (vehicle is null) return NotFound();

            PhotoCount = vehicle.Photos.Count;
            Input = new InputModel
            {
                Vin = vehicle.Vin,
                Reg = vehicle.Reg,
                MakeModel = vehicle.MakeModel
            };

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                await LoadPhotoCountAsync();
                return Page();
            }

            var result = await _editor.UpdateAsync(
                Id, Input.Vin, Input.Reg, Input.MakeModel, _userManager.GetUserId(User)!);

            if (result.Status == VehicleEditStatus.NotFound) return NotFound();

            if (result.Status != VehicleEditStatus.Updated)
            {
                // Conflicts and the no-identifier case re-render the form with what the user typed
                // still in place, so they can fix the one field that's wrong.
                ErrorMessage = result.Message;
                await LoadPhotoCountAsync();
                return Page();
            }

            return RedirectToPage("/Vehicles/Details", new { id = Id });
        }

        private async Task LoadPhotoCountAsync()
        {
            PhotoCount = await _db.Photos.CountAsync(p => p.VehicleId == Id);
        }
    }
}
