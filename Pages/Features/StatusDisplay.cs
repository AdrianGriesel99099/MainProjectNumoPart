using MainProjectNumoPart.Models;

namespace MainProjectNumoPart.Pages.Features
{
    // Shared between Index.cshtml and Details.cshtml so the two pages can't drift on what a
    // status is called or coloured.
    public static class StatusDisplay
    {
        public static string Label(FeatureProposalStatus status) => status switch
        {
            FeatureProposalStatus.NeedsReview => "Needs review",
            FeatureProposalStatus.AwaitingAiRevision => "Claude is revising",
            FeatureProposalStatus.ReadyForFinalApproval => "Ready for final approval",
            FeatureProposalStatus.Approved => "Approved — queued to build",
            FeatureProposalStatus.Denied => "Denied",
            _ => status.ToString()
        };

        public static string BadgeClass(FeatureProposalStatus status) => status switch
        {
            FeatureProposalStatus.NeedsReview => "bg-warning text-dark",
            FeatureProposalStatus.AwaitingAiRevision => "bg-secondary",
            FeatureProposalStatus.ReadyForFinalApproval => "bg-info text-dark",
            FeatureProposalStatus.Approved => "bg-success",
            FeatureProposalStatus.Denied => "bg-danger",
            _ => "bg-secondary"
        };
    }
}
