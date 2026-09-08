namespace MainProjectNumoPart.Models
{
    // A pinned point recording where damage is, and what it is. Click a part, a popup opens,
    // click inside it to drop a pin — this is the record that click creates.
    public class DamageMark
    {
        public int Id { get; set; }
        public int VehicleId { get; set; }
        public Vehicle Vehicle { get; set; } = null!;

        // Captured directly, not inferred from Photo.Part, and never changes after the mark is
        // made — the same reasoning as AuthorEmail on PhotoComment/VehicleUpdate (captured at
        // write time so it stays accurate forever): if the photo this mark is anchored to gets
        // re-tagged to a different part later, a mark that inferred its part from Photo.Part
        // would silently become wrong. This can't.
        public Part Part { get; set; }

        // Null = no photo was tagged to this part yet when the mark was made, so it has nothing
        // to be anchored to — XPercent/YPercent are then a fixed centred position rather than a
        // real pin (see damagemarks.js's no-visual fallback).
        public int? PhotoId { get; set; }
        public Photo? Photo { get; set; }

        // Position as a PERCENTAGE of whatever image is displayed, 0-100. Never pixel
        // coordinates — the mark must render correctly regardless of how large the image is
        // drawn (a thumbnail in a list vs. full-size in the popup).
        public double XPercent { get; set; }
        public double YPercent { get; set; }

        public string Note { get; set; } = null!;
        public string AuthorId { get; set; } = null!;
        public string AuthorEmail { get; set; } = null!;
        public DateTime CreatedAtUtc { get; set; }
    }
}
