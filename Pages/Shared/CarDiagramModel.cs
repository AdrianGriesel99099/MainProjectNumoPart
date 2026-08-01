using System.Collections.Generic;
using MainProjectNumoPart.Models;

namespace MainProjectNumoPart.Pages.Shared
{
    // View model for _CarDiagram.cshtml.
    public class CarDiagramModel
    {
        // "coverage" shades parts that already have photos and filters on click.
        // "picker" selects a part — also the fallback when WebGL is unavailable.
        public string Mode { get; set; } = "picker";

        // Distinct id so two diagrams can appear on one page without their scripts colliding.
        public string ElementId { get; set; } = "car-diagram";

        // Photo count per part. Only consulted in coverage mode; a part absent from the
        // dictionary has no photos.
        public IReadOnlyDictionary<Part, int> PhotoCounts { get; set; } = new Dictionary<Part, int>();
    }
}
