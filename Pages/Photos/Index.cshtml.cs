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

        public List<Photo> Photos { get; set; } = new();

        public async Task OnGetAsync()
        {
            var untagged = PartFilter == "untagged";
            Part? part = !untagged && Enum.TryParse<Part>(PartFilter, out var parsed) ? parsed : null;

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

            Photos = await PhotoFilterQuery.Apply(_db.Photos.Include(p => p.Vehicle), filter)
                .Take(120)
                .ToListAsync();
        }
    }
}
