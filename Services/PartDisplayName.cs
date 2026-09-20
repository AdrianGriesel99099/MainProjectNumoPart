using System.ComponentModel.DataAnnotations;
using System.Linq;
using MainProjectNumoPart.Models;

namespace MainProjectNumoPart.Services
{
    // Html.DisplayFor(_ => part) reads Part's [Display(Name=...)] attributes for the UI; this is
    // the same lookup for the export builders, which run outside Razor and have no ModelMetadata
    // to ask.
    public static class PartDisplayName
    {
        public static string Get(Part part)
        {
            var member = typeof(Part).GetMember(part.ToString()).FirstOrDefault();
            var display = member?.GetCustomAttributes(typeof(DisplayAttribute), false)
                .Cast<DisplayAttribute>().FirstOrDefault();
            return display?.Name ?? part.ToString();
        }
    }
}
