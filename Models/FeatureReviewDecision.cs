namespace MainProjectNumoPart.Models
{
    // A human reviewer's decision on one review round. TooComplex is a distinct outcome from
    // Revised: the idea itself is wanted, but as currently scoped it's too much to take on --
    // unlike Revised (generic feedback, comment required), a comment here is optional since the
    // reason is self-explanatory, and it's a signal to the review bot to cut scope down, not just
    // add detail. See CLAUDE.md's "Feature proposals & review" section.
    public enum FeatureReviewDecision
    {
        Accepted,
        Revised,
        Denied,
        TooComplex
    }
}
