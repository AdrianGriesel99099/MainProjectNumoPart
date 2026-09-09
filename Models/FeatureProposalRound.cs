using System;
using System.Collections.Generic;

namespace MainProjectNumoPart.Models
{
    // One cycle of the review workflow. Round 0 has AiContent == null -- it IS the original
    // FeatureProposal.Description, shown as-is for review. Every round after that carries the
    // review-bot's revised write-up in AiContent, plus whatever Questions it decided were worth
    // asking directly (can be empty -- most rounds won't have any). HumanDecision/HumanComment/
    // DecidedBy/DecidedAtUtc stay null until a reviewer acts on this specific round.
    public class FeatureProposalRound
    {
        public int Id { get; set; }
        public int FeatureProposalId { get; set; }
        public FeatureProposal FeatureProposal { get; set; } = null!;
        public int RoundNumber { get; set; }
        public string? AiContent { get; set; }

        public List<FeatureProposalQuestion> Questions { get; set; } = new();

        public FeatureReviewDecision? HumanDecision { get; set; }
        public string? HumanComment { get; set; }
        public string? DecidedByUserId { get; set; }
        public DateTime? DecidedAtUtc { get; set; }

        public DateTime CreatedAtUtc { get; set; }
    }
}
