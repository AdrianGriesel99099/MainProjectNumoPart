using System.ComponentModel.DataAnnotations;

namespace MainProjectNumoPart.Models
{
    // Which panel of the car a photo shows. Independent of Stage: a photo has both a stage
    // (when in the job it was taken) and a part (what it shows). Nullable on Photo — null means
    // "not tagged yet", which is the normal state straight after a bulk upload.
    //
    // EXPLICIT VALUES ARE LOAD-BEARING. EF Core persists enums as int (verified: Stage is
    // `int` in both providers' migrations), so the number IS what lives in the database.
    // Without explicit values, inserting a member in the middle would silently re-label every
    // existing photo — a bug that surfaces months later as "the wrong panel". Append new parts
    // with the next free number; never renumber an existing one.
    //
    // [Display] names drive Html.GetEnumSelectList<Part>() with no extra plumbing, and the
    // member names are also the mesh names in the generated 3D model — CarModelManifestTests
    // pins that correspondence.
    public enum Part
    {
        [Display(Name = "Front bumper")] FrontBumper = 1,
        [Display(Name = "Bonnet")] Bonnet = 2,
        [Display(Name = "Left headlight")] HeadlightLeft = 3,
        [Display(Name = "Right headlight")] HeadlightRight = 4,
        [Display(Name = "Windscreen")] Windscreen = 5,

        [Display(Name = "Roof")] Roof = 6,
        [Display(Name = "Rear windscreen")] RearWindscreen = 7,
        [Display(Name = "Boot / tailgate")] BootTailgate = 8,
        [Display(Name = "Rear bumper")] RearBumper = 9,
        [Display(Name = "Left tail light")] TaillightLeft = 10,
        [Display(Name = "Right tail light")] TaillightRight = 11,

        [Display(Name = "Left front wing")] WingFrontLeft = 12,
        [Display(Name = "Right front wing")] WingFrontRight = 13,
        [Display(Name = "Front left door")] DoorFrontLeft = 14,
        [Display(Name = "Front right door")] DoorFrontRight = 15,
        [Display(Name = "Rear left door")] DoorRearLeft = 16,
        [Display(Name = "Rear right door")] DoorRearRight = 17,
        [Display(Name = "Left rear quarter")] QuarterRearLeft = 18,
        [Display(Name = "Right rear quarter")] QuarterRearRight = 19,
        [Display(Name = "Left mirror")] MirrorLeft = 20,
        [Display(Name = "Right mirror")] MirrorRight = 21,
        [Display(Name = "Left sill")] SillLeft = 22,
        [Display(Name = "Right sill")] SillRight = 23,

        [Display(Name = "Front left wheel")] WheelFrontLeft = 24,
        [Display(Name = "Front right wheel")] WheelFrontRight = 25,
        [Display(Name = "Rear left wheel")] WheelRearLeft = 26,
        [Display(Name = "Rear right wheel")] WheelRearRight = 27,

        // These six have no honest position on a drawing of the car's outside, so the UI offers
        // them as plain buttons rather than inventing hotspots for them.
        [Display(Name = "Interior")] Interior = 28,
        [Display(Name = "Engine bay")] EngineBay = 29,
        [Display(Name = "Boot interior")] LoadArea = 30,
        [Display(Name = "Odometer / dash")] Odometer = 31,
        [Display(Name = "Undercarriage")] Undercarriage = 32,
        [Display(Name = "Other / general")] Other = 33
    }
}
