using System;
using System.Collections.Generic;

namespace MainProjectNumoPart.Models
{
    // A candidate feature someone would like built. Round 0 (see FeatureProposalRound) is always
    // the raw submission itself; every later round is a revision the nightly review-bot workflow
    // (Tools/FeatureReviewBot, .github/workflows/feature-review.yml) drafted after a human asked
    // for changes. QueuedForBuildAtUtc is set once this proposal has been written into
    // docs/BACKLOG.md, so it isn't queued there twice.
    public class FeatureProposal
    {
        public int Id { get; set; }
        public string Title { get; set; } = null!;
        public string Description { get; set; } = null!;
        public string SubmittedByUserId { get; set; } = null!;

        // See PhotoComment.AuthorEmail for why this is captured at write time, not resolved live.
        public string SubmittedByEmail { get; set; } = null!;

        public FeatureProposalStatus Status { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? QueuedForBuildAtUtc { get; set; }

        public List<FeatureProposalRound> Rounds { get; set; } = new();
    }
}
