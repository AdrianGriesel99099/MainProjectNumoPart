using System.Collections.Generic;
using System.Threading.Tasks;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MainProjectNumoPart.Pages.Features
{
    // Mutations (submitting an idea, deciding on one) go through
    // Endpoints/FeatureProposalEndpoints.cs, not page handlers -- see that file for why (Razor
    // Pages has no per-handler [Authorize], and this page is viewable by every signed-in role).
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly FeatureProposalService _proposals;

        public IndexModel(FeatureProposalService proposals)
        {
            _proposals = proposals;
        }

        public List<FeatureProposal> Proposals { get; private set; } = new();

        public async Task OnGetAsync()
        {
            Proposals = await _proposals.ListAsync();
        }
    }
}
