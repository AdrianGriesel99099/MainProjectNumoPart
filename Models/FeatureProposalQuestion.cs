using System.Collections.Generic;

namespace MainProjectNumoPart.Models
{
    // A multiple-choice question the review-bot drafted alongside one round's revised write-up,
    // when it decided there's a genuine fork worth putting to the reviewer directly rather than
    // leaving it to the free-text comment alone. SelectedOptionId stays null until answered --
    // answering is optional, same as the comment box it sits alongside (see
    // Services/FeatureProposalService.cs's DecideAsync).
    public class FeatureProposalQuestion
    {
        public int Id { get; set; }
        public int FeatureProposalRoundId { get; set; }
        public FeatureProposalRound Round { get; set; } = null!;
        public int QuestionNumber { get; set; }
        public string Prompt { get; set; } = null!;

        public List<FeatureProposalQuestionOption> Options { get; set; } = new();

        public int? SelectedOptionId { get; set; }
        public FeatureProposalQuestionOption? SelectedOption { get; set; }
    }
}
