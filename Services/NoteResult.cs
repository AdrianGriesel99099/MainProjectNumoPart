namespace MainProjectNumoPart.Services
{
    // Shared between VehicleUpdateService and PhotoCommentService — both do the identical shape
    // of work (validate a free-text body, confirm the parent still exists, save with attribution),
    // so one result type avoids two copies of the same four states. The entities themselves stay
    // fully separate tables (PhotoComment / VehicleUpdate) — this is shared plumbing, not a shared
    // "Note" data model.
    public enum NoteStatus
    {
        Success,
        NotFound,
        EmptyBody,
        TooLong
    }

    public record NoteResult(NoteStatus Status, string? Message = null);
}
