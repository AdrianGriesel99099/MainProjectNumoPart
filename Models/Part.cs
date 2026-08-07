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

        [Display(Name = "Front spoiler")] FrontSpoiler = 34,
        [Display(Name = "Rear spoiler")] RearSpoiler = 35,
        [Display(Name = "Main grill")] MainGrill = 36,
        [Display(Name = "Centre grill")] CentreGrill = 37,
        [Display(Name = "Left spotlamp")] SpotlampLeft = 38,
        [Display(Name = "Right spotlamp")] SpotlampRight = 39,
        [Display(Name = "Left spotlamp grill")] SpotlampGrillLeft = 40,
        [Display(Name = "Right spotlamp grill")] SpotlampGrillRight = 41,
        [Display(Name = "Left front bumper grill")] BumperGrillFrontLeft = 42,
        [Display(Name = "Right front bumper grill")] BumperGrillFrontRight = 43,

        // These six have no honest position on a drawing of the car's outside, so the UI offers
        // them as plain buttons rather than inventing hotspots for them.
        [Display(Name = "Interior")] Interior = 28,
        [Display(Name = "Engine bay")] EngineBay = 29,
        [Display(Name = "Boot interior")] LoadArea = 30,
        [Display(Name = "Odometer / dash")] Odometer = 31,
        [Display(Name = "Undercarriage")] Undercarriage = 32,
        [Display(Name = "Other / general")] Other = 33,

        // Fenderliners (inside the wheel arch) and bumper slides (mounting brackets behind the
        // bumper cover) join the six above for the same reason: not visible from outside the
        // car, so a diagram/3D hotspot for them would be an invented position, not a real one.
        [Display(Name = "Left front fenderliner")] FenderlinerFrontLeft = 44,
        [Display(Name = "Right front fenderliner")] FenderlinerFrontRight = 45,
        [Display(Name = "Left front bumper slide")] BumperSlideFrontLeft = 46,
        [Display(Name = "Right front bumper slide")] BumperSlideFrontRight = 47,
        [Display(Name = "Left rear bumper slide")] BumperSlideRearLeft = 48,
        [Display(Name = "Right rear bumper slide")] BumperSlideRearRight = 49,

        // The 3D reference build this picker's geometry was ported from treats these as real,
        // individually clickable/inspectable parts, not decoration — the earlier port had them
        // visible in 3D but not selectable here. Added as their own parts to match.
        [Display(Name = "Cowl panel")] CowlPanel = 50,
        [Display(Name = "Left A-pillar")] PillarALeft = 51,
        [Display(Name = "Right A-pillar")] PillarARight = 52,
        [Display(Name = "Left B-pillar")] PillarBLeft = 53,
        [Display(Name = "Right B-pillar")] PillarBRight = 54,
        [Display(Name = "Left C-pillar")] PillarCLeft = 55,
        [Display(Name = "Right C-pillar")] PillarCRight = 56,
        [Display(Name = "Left drip rail")] DripRailLeft = 57,
        [Display(Name = "Right drip rail")] DripRailRight = 58,
        [Display(Name = "Front left door glass")] DoorGlassFrontLeft = 59,
        [Display(Name = "Front right door glass")] DoorGlassFrontRight = 60,
        [Display(Name = "Rear left door glass")] DoorGlassRearLeft = 61,
        [Display(Name = "Rear right door glass")] DoorGlassRearRight = 62,
        [Display(Name = "Left quarter glass")] QuarterGlassLeft = 63,
        [Display(Name = "Right quarter glass")] QuarterGlassRight = 64,
        [Display(Name = "Centre garnish")] CentreGarnish = 65,
        [Display(Name = "Rear valance")] RearValance = 66,
        [Display(Name = "Front left door handle")] DoorHandleFrontLeft = 67,
        [Display(Name = "Front right door handle")] DoorHandleFrontRight = 68,
        [Display(Name = "Rear left door handle")] DoorHandleRearLeft = 69,
        [Display(Name = "Rear right door handle")] DoorHandleRearRight = 70,
        [Display(Name = "Left side repeater")] SideRepeaterLeft = 71,
        [Display(Name = "Right side repeater")] SideRepeaterRight = 72,
        [Display(Name = "Wipers")] Wipers = 73,
        [Display(Name = "Front number plate")] FrontPlate = 74,
        [Display(Name = "Rear number plate")] RearPlate = 75,
        [Display(Name = "Exhaust tips")] ExhaustTips = 76,
        [Display(Name = "Antenna")] Antenna = 77,
        [Display(Name = "Fuel filler")] FuelFiller = 78
    }
}
