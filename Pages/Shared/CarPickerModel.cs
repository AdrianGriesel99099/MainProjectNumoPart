namespace MainProjectNumoPart.Pages.Shared
{
    // View model for _CarPicker.cshtml — the 3D picker plus its 2D fallback.
    public class CarPickerModel
    {
        // Distinct id so two pickers can appear on one page without their scripts colliding.
        public string ElementId { get; set; } = "car-picker";

        // Name of the hidden <select> / form field the picker should keep in sync so a plain
        // form post still works with no JS at all beyond the fallback diagram.
        public string SelectElementId { get; set; } = "";
    }
}
