namespace MainProjectNumoPart.Models
{
    // EF Core stores this enum as int, so the number IS the database value (same concern as
    // Models/Part.cs). Append new stages at the bottom — never insert in the middle — or every
    // existing photo silently re-labels itself to a different stage.
    public enum Stage
    {
        Checkin,
        Quote,
        Progress,
        Checkout,
        Extra,
        WheelAlignment,
        Diagnostics
    }
}
