using System;
using System.Collections.Generic;

namespace MainProjectNumoPart.Services
{
    // Word-wraps free text (a photo comment) into lines that fit a given width, using a caller
    // supplied measurement function so this stays testable without a real font/graphics context —
    // the PDF builder passes XGraphics.MeasureString, tests pass plain string length.
    public static class PhotoExportTextWrap
    {
        public static List<string> Wrap(string text, Func<string, double> measureWidth, double maxWidth)
        {
            var lines = new List<string>();
            var normalized = (text ?? string.Empty).Replace("\r\n", "\n");

            foreach (var paragraph in normalized.Split('\n'))
            {
                if (paragraph.Length == 0)
                {
                    lines.Add(string.Empty);
                    continue;
                }

                var current = string.Empty;
                foreach (var word in paragraph.Split(' '))
                {
                    var candidate = current.Length == 0 ? word : current + " " + word;
                    // current.Length == 0 also covers a single word wider than maxWidth on its
                    // own — kept whole rather than dropped or character-split.
                    if (current.Length == 0 || measureWidth(candidate) <= maxWidth)
                    {
                        current = candidate;
                    }
                    else
                    {
                        lines.Add(current);
                        current = word;
                    }
                }
                lines.Add(current);
            }

            return lines;
        }
    }
}
