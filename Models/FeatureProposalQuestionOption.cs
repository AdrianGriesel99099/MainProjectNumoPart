namespace MainProjectNumoPart.Models
{
    // One choice offered on a FeatureProposalQuestion. Options belong to exactly one question and
    // are never reused across questions, even if the label happens to repeat.
    public class FeatureProposalQuestionOption
    {
        public int Id { get; set; }
        public int FeatureProposalQuestionId { get; set; }
        public FeatureProposalQuestion Question { get; set; } = null!;
        public int OptionNumber { get; set; }
        public string Label { get; set; } = null!;
    }
}
