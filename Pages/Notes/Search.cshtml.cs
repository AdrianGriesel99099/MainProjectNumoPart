using System.Collections.Generic;
using System.Threading.Tasks;
using MainProjectNumoPart.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MainProjectNumoPart.Pages.Notes
{
    // [Authorize] only, not role-restricted — reading job-card updates, photo comments, and
    // damage-mark notes is open to every signed-in role, same as the pages that show them.
    [Authorize]
    public class SearchModel : PageModel
    {
        private readonly NotesSearchService _notesSearchService;

        public SearchModel(NotesSearchService notesSearchService)
        {
            _notesSearchService = notesSearchService;
        }

        [BindProperty(SupportsGet = true)]
        public string? Query { get; set; }

        public List<NoteSearchResult> Results { get; set; } = new();

        public bool HasSearched { get; set; }

        public async Task OnGetAsync()
        {
            HasSearched = !string.IsNullOrWhiteSpace(Query);
            Results = await _notesSearchService.SearchAsync(Query);
        }
    }
}
