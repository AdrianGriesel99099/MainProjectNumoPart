using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MainProjectNumoPart.Authorization;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MainProjectNumoPart.Pages
{
    // A personal "what did I do today" feed -- a shift-end check that nothing was missed, since
    // none of the per-vehicle pages show a cross-vehicle view of one person's own work.
    [Authorize(Roles = Roles.StaffOrAdmin)]
    public class MyActivityModel : PageModel
    {
        private readonly AppDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;

        public MyActivityModel(AppDbContext db, UserManager<IdentityUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        [BindProperty(SupportsGet = true)] public DateTime? Date { get; set; }

        public DateOnly SelectedDate { get; private set; }
        public List<MyActivityItem> Items { get; private set; } = new();

        public async Task OnGetAsync()
        {
            SelectedDate = Date.HasValue ? DateOnly.FromDateTime(Date.Value) : DateOnly.FromDateTime(DateTime.UtcNow);

            var fromUtc = DateTime.SpecifyKind(SelectedDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            var toUtc = fromUtc.AddDays(1);
            var userId = _userManager.GetUserId(User)!;

            Items = await MyActivityService.ForUserAsync(_db, userId, fromUtc, toUtc);
        }
    }
}
