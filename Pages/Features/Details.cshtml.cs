using System.Threading.Tasks;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MainProjectNumoPart.Pages.Features
{
    [Authorize]
    public class DetailsModel : PageModel
    {
        private readonly FeatureProposalService _proposals;

        public DetailsModel(FeatureProposalService proposals)
        {
            _proposals = proposals;
        }

        public FeatureProposal? Proposal { get; private set; }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            Proposal = await _proposals.GetWithRoundsAsync(id);
            return Proposal is null ? NotFound() : Page();
        }
    }
}
