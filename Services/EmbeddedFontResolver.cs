using System;
using System.IO;
using System.Reflection;
using PdfSharpCore.Fonts;

namespace MainProjectNumoPart.Services
{
    // PdfSharpCore's default Linux font lookup shells out to fontconfig, which finds nothing on
    // this app's production image (mcr.microsoft.com/dotnet/aspnet:8.0 ships no fonts at all) —
    // confirmed by that image's own Dockerfile, which installs nothing font-related. Resolving to
    // a font embedded in this assembly instead means PDF export needs nothing from the host,
    // dev machine or container alike. See Data/Fonts/DejaVuSans-LICENSE.txt for the font's license.
    internal class EmbeddedFontResolver : IFontResolver
    {
        public const string FamilyName = "DejaVu Sans";

        private const string ResourceName = "MainProjectNumoPart.Data.Fonts.DejaVuSans.ttf";

        public string DefaultFontName => FamilyName;

        public byte[] GetFont(string faceName)
        {
            using var stream = typeof(EmbeddedFontResolver).Assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException($"Embedded font resource '{ResourceName}' not found.");
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }

        // Only one face is embedded, so every request (bold, italic, or not) resolves to it —
        // callers get plain weight text regardless of the XFontStyle they ask for. PhotoExportPdfBuilder
        // never asks for anything else, using size rather than weight for emphasis.
        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
            => new(FamilyName);
    }
}
