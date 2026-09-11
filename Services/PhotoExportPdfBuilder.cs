using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using PdfSharpCore;
using PdfSharpCore.Drawing;
using PdfSharpCore.Fonts;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.Advanced;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace MainProjectNumoPart.Services
{
    // Builds the "PDF" export format: one photo per page (spilling onto extra pages when its
    // comment thread is too long to fit), each with the image, then its stage/part/date, then its
    // comment thread as plain text — never burned onto the image itself; only damage-mark pins
    // are (see DamageMarkImageAnnotator), and only when the caller asked for them.
    public static class PhotoExportPdfBuilder
    {
        private const double PageMargin = 40;
        private const double MaxImageHeightFraction = 0.55;

        // GlobalFontSettings.FontResolver is a process-wide static in PdfSharpCore — this is the
        // one place in the app that draws PDF text, so setting it here (once, on first use) rather
        // than from Program.cs keeps that wiring next to the only code that needs it.
        static PhotoExportPdfBuilder()
        {
            GlobalFontSettings.FontResolver = new EmbeddedFontResolver();
        }

        public static byte[] Build(IReadOnlyList<PhotoExportItem> items)
        {
            var document = new PdfDocument();
            var headerFont = new XFont(EmbeddedFontResolver.FamilyName, 12, XFontStyle.Regular);
            var bodyFont = new XFont(EmbeddedFontResolver.FamilyName, 10, XFontStyle.Regular);

            foreach (var item in items)
            {
                var cursor = new PageCursor(document, PageMargin);
                DrawImage(cursor, item);
                DrawDetails(cursor, item, headerFont, bodyFont);
            }

            using var output = new MemoryStream();
            document.Save(output);
            return output.ToArray();
        }

        private static void DrawImage(PageCursor cursor, PhotoExportItem item)
        {
            // Deliberately not XImage/PdfImage (XImage.FromStream, XImage.FromImageSource, or
            // Gfx.DrawImage with any XImage at all): every one of those routes through
            // PdfSharpCore's internal ImageSharpImageSource, which was built against
            // SixLabors.ImageSharp 1.0.4. Confirmed at runtime (a clean build hides all of this —
            // no compile error) against the 3.1.12 this app pins: its stream loader calls an
            // Image.Load(Stream, out IImageFormat) overload that no longer exists, and both its
            // JPEG and raw-bitmap embed paths construct `new JpegEncoder`/`new BmpEncoder` and set
            // an option property that became init-only somewhere between 1.0.4 and 3.1.12 — a
            // caller compiled against the old mutable setter can't bind to it
            // (MissingMethodException). Every embed path was tried; all three fail the same way.
            // Since the bytes here are already a JPEG we produced ourselves (DamageMarkImageAnnotator
            // or NormalizeToJpegAsync both always emit one), embedding it as a raw DCTDecode
            // XObject via PdfSharpCore's low-level Pdf object model sidesteps its image pipeline
            // entirely — no ImageSharp call happens on PdfSharpCore's side at all.
            using var decoded = SixLabors.ImageSharp.Image.Load<Rgba32>(item.ImageBytes);
            using var jpegStream = new MemoryStream();
            decoded.Save(jpegStream, new JpegEncoder { Quality = 90 });
            var jpegBytes = jpegStream.ToArray();

            var maxWidth = cursor.ContentWidth;
            var maxHeight = cursor.PageHeight * MaxImageHeightFraction;
            var scale = Math.Min(maxWidth / decoded.Width, maxHeight / decoded.Height);
            var drawWidth = decoded.Width * scale;
            var drawHeight = decoded.Height * scale;
            var x = cursor.Margin + (maxWidth - drawWidth) / 2;

            DrawJpegXObject(cursor, jpegBytes, decoded.Width, decoded.Height, x, cursor.Y, drawWidth, drawHeight);
            cursor.Advance(drawHeight + 14);
        }

        private static void DrawJpegXObject(
            PageCursor cursor, byte[] jpegBytes, int pixelWidth, int pixelHeight,
            double x, double yFromTop, double width, double height)
        {
            var imageDict = new PdfDictionary(cursor.Document);
            imageDict.Elements["/Type"] = new PdfName("/XObject");
            imageDict.Elements["/Subtype"] = new PdfName("/Image");
            imageDict.Elements["/Width"] = new PdfInteger(pixelWidth);
            imageDict.Elements["/Height"] = new PdfInteger(pixelHeight);
            imageDict.Elements["/BitsPerComponent"] = new PdfInteger(8);
            imageDict.Elements["/ColorSpace"] = new PdfName("/DeviceRGB");
            imageDict.Elements["/Filter"] = new PdfName("/DCTDecode");
            imageDict.CreateStream(jpegBytes);
            // A stream must always be an indirect object per the PDF spec (unlike a plain
            // dictionary, which can be embedded inline) — this is what makes .Reference below
            // valid. PdfResources has no public way to register an XObject short of AddImage(
            // PdfImage), which needs the same broken PdfImage type this whole method exists to
            // avoid, so the /XObject sub-dictionary is built and attached by hand instead — both
            // are just plain PDF dictionaries under the hood, no different from any other.
            cursor.Document.Internals.AddObject(imageDict);

            // One image per page, so the name doesn't need to be unique beyond this page, and a
            // fresh page never already has an /XObject entry to merge with.
            const string imageResourceName = "/Im0";
            var xObjectDictionary = new PdfDictionary(cursor.Document);
            xObjectDictionary.Elements[imageResourceName] = imageDict.Reference;
            cursor.Page.Resources.Elements["/XObject"] = xObjectDictionary;

            var ci = CultureInfo.InvariantCulture;
            var pdfBottomY = cursor.PageHeight - yFromTop - height;
            var contentText =
                "q\n" +
                $"{width.ToString(ci)} 0 0 {height.ToString(ci)} {x.ToString(ci)} {pdfBottomY.ToString(ci)} cm\n" +
                $"{imageResourceName} Do\n" +
                "Q\n";
            cursor.Page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes(contentText));
        }

        private static void DrawDetails(PageCursor cursor, PhotoExportItem item, XFont headerFont, XFont bodyFont)
        {
            var partLabel = item.Part.HasValue ? PartDisplayName.Get(item.Part.Value) : "Untagged";
            cursor.WriteLine($"Stage: {item.Stage}    Part: {partLabel}    Date: {item.UploadedAtUtc:d MMM yyyy, HH:mm} UTC", headerFont);
            cursor.Advance(8);

            if (item.Comments.Count == 0)
            {
                cursor.WriteLine("No comments.", bodyFont);
                return;
            }

            cursor.WriteLine("Comments:", headerFont);
            foreach (var comment in item.Comments)
            {
                cursor.WriteLine($"{comment.AuthorEmail} — {comment.CreatedAtUtc:d MMM yyyy, HH:mm}", bodyFont);
                var wrapWidth = cursor.ContentWidth - 12;
                foreach (var line in PhotoExportTextWrap.Wrap(comment.Body, s => cursor.Gfx.MeasureString(s, bodyFont).Width, wrapWidth))
                {
                    cursor.WriteLine("    " + line, bodyFont);
                }
                cursor.Advance(4);
            }
        }

        // Tracks where the next line goes and starts a new page automatically once the current
        // one runs out of room — so a photo with a long comment thread spills onto extra pages
        // instead of being silently cut off at the bottom margin.
        private sealed class PageCursor
        {
            private readonly PdfDocument _document;

            public PdfDocument Document => _document;
            public PdfPage Page { get; private set; } = null!;
            public double Margin { get; }
            public XGraphics Gfx { get; private set; } = null!;
            public double Y { get; private set; }
            public double PageHeight { get; private set; }
            public double PageWidth { get; private set; }
            public double ContentWidth => PageWidth - 2 * Margin;

            public PageCursor(PdfDocument document, double margin)
            {
                _document = document;
                Margin = margin;
                NewPage();
            }

            public void Advance(double amount) => Y += amount;

            public void WriteLine(string text, XFont font)
            {
                var lineHeight = font.Height * 1.2;
                if (Y + lineHeight > PageHeight - Margin)
                {
                    NewPage();
                }

                Gfx.DrawString(text, font, XBrushes.Black, new XPoint(Margin, Y + font.Height));
                Y += lineHeight;
            }

            private void NewPage()
            {
                Page = _document.AddPage();
                Page.Size = PageSize.A4;
                Gfx = XGraphics.FromPdfPage(Page);
                PageHeight = Page.Height.Point;
                PageWidth = Page.Width.Point;
                Y = Margin;
            }
        }
    }
}
