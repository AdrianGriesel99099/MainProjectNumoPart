namespace MainProjectNumoPart.Models
{
    // The proposal review/approval workflow's state. EF Core persists this as an int (see
    // Part.cs for why explicit values matter) -- append new members with the next free number,
    // never renumber or reuse one.
    public enum FeatureProposalStatus
    {
        NeedsReview = 1,           // awaiting a human decision -- the raw submission, or a round the bot just revised
        AwaitingAiRevision = 2,    // human said continue/revise -- queued for the nightly review-bot workflow
        ReadyForFinalApproval = 3, // the bot's latest revision believes nothing more needs adding
        Approved = 4,              // final approval given -- queued to be written into docs/BACKLOG.md
        Denied = 5                 // terminal
    }
}
