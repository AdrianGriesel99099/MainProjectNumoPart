using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MainProjectNumoPart.Pages
{
    [Authorize]
    public class ChangelogModel : PageModel
    {
        public record Entry(
            [property: JsonPropertyName("date")] DateOnly Date,
            [property: JsonPropertyName("title")] string Title,
            [property: JsonPropertyName("description")] string Description,
            // Set only when a shipped feature traces back to an approved proposal (see
            // Pages/Features) -- the evening deploy routine fills this in from the merged PR's
            // "(proposal #N)" tag. Absent for everything else, including this file's seed entries.
            [property: JsonPropertyName("link")] string? Link = null);

        // Grouped by date (newest first) rather than a flat list -- several entries commonly
        // land the same day, and a repeated date on every row would just be visual noise next
        // to the same value.
        public List<IGrouping<DateOnly, Entry>> Groups { get; private set; } = new();

        public void OnGet()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Data", "changelog.json");
            if (!System.IO.File.Exists(path))
            {
                return; // Empty is a valid, renderable state -- see the .cshtml's empty-state markup.
            }

            var entries = JsonSerializer.Deserialize<List<Entry>>(System.IO.File.ReadAllText(path))
                ?? new List<Entry>();

            Groups = entries
                .OrderByDescending(e => e.Date)
                .GroupBy(e => e.Date)
                .ToList();
        }
    }
}
