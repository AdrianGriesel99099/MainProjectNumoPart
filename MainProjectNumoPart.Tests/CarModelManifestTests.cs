using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MainProjectNumoPart.Models;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    // The 3D picker binds clicks to Part enum members, via mesh/group names assigned in
    // wwwroot/js/car-body.js (e.g. "B.FrontBumper = [...]"). Nothing in the compiler enforces
    // that a Part member and a car-body.js assignment stay in sync: rename one without the other
    // and you get a panel that can never be selected — invisible until someone tries to tag that
    // exact part, possibly months later during a dispute. These tests are what stands between
    // that rename and a silent hole in the UI.
    //
    // There is no build step to regenerate a manifest from any more (car-body.js IS the model —
    // real parametric geometry generated at runtime, not a loaded .glb), so unlike the Blender-era
    // version of this test, the source of truth is read directly out of the JS file's own text.
    public class CarModelManifestTests
    {
        // Six parts have no honest position on the outside of a car (interior, engine bay, boot
        // interior, dash, undercarriage, "other") — Part.cs's own comment documents this, and the
        // 2D diagram and 3D picker both offer them as plain buttons instead of inventing a
        // hotspot. They are the only Part members that are correctly ABSENT from car-body.js.
        private static readonly HashSet<string> ButtonOnly = new(StringComparer.Ordinal)
        {
            "Interior", "EngineBay", "LoadArea", "Odometer", "Undercarriage", "Other"
        };

        private static readonly string[] RegisteredParts = LoadRegisteredParts();

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "wwwroot")))
            {
                dir = dir.Parent;
            }
            return dir?.FullName
                ?? throw new InvalidOperationException("Could not locate the repo root from the test output directory.");
        }

        private static string CarBodyPath() => Path.Combine(RepoRoot(), "wwwroot", "js", "car-body.js");

        private static string[] LoadRegisteredParts()
        {
            var path = CarBodyPath();
            if (!File.Exists(path))
            {
                throw new InvalidOperationException($"car-body.js not found at {path}.");
            }

            var source = File.ReadAllText(path);
            // Matches "B.PartName = " at the start of a (trimmed) line — the pattern every
            // clickable-part registration in car-body.js follows. A part built across several
            // real meshes (e.g. B.FrontBumper = [capMesh(...), bp(...), bp(...), bp(...)]) is
            // still just one such assignment, matching this test's intent exactly: one entry per
            // Part member, however many meshes it takes to draw it.
            return Regex.Matches(source, @"^\s*B\.([A-Za-z]+)\s*=", RegexOptions.Multiline)
                .Select(m => m.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        [Fact]
        public void EveryRegisteredNameIsARealPart()
        {
            var unknown = RegisteredParts.Where(m => !Enum.TryParse<Part>(m, out _)).ToList();

            Assert.True(unknown.Count == 0,
                "car-body.js registers names that are not Part values: " + string.Join(", ", unknown) +
                ". The enum member was likely renamed without updating car-body.js.");
        }

        [Fact]
        public void EveryVisiblePartExistsInCarBody()
        {
            var present = new HashSet<string>(RegisteredParts, StringComparer.Ordinal);
            var missing = Enum.GetNames<Part>()
                .Where(n => !ButtonOnly.Contains(n) && !present.Contains(n))
                .ToList();

            Assert.True(missing.Count == 0,
                "These parts have no registration in car-body.js, so they can never be picked in 3D: " +
                string.Join(", ", missing) + ". If a part is genuinely not visible from outside the " +
                "car, add it to ButtonOnly above instead of leaving this failing.");
        }

        [Fact]
        public void ButtonOnlyPartsAreNotAccidentallyRegistered()
        {
            // The inverse check: if one of the six button-only parts DOES show up in car-body.js,
            // either it grew a real 3D position (in which case remove it from ButtonOnly, don't
            // just relax this test) or it is a stray/duplicate registration worth investigating.
            var present = new HashSet<string>(RegisteredParts, StringComparer.Ordinal);
            var unexpected = ButtonOnly.Where(present.Contains).ToList();

            Assert.True(unexpected.Count == 0,
                "These were assumed button-only (no honest 3D position) but are registered in " +
                "car-body.js: " + string.Join(", ", unexpected) + ". Update ButtonOnly above to match.");
        }

        [Fact]
        public void CarBodyScriptIsSmallEnoughForAPhone()
        {
            var script = new FileInfo(CarBodyPath());

            Assert.True(script.Exists, $"car-body.js is missing at {script.FullName}.");

            // The picker sits on the upload page, which staff open on mobile data in a workshop.
            // A model that takes seconds to arrive delays the form behind it, so this is a real
            // budget rather than tidiness — if it trips, simplify the geometry rather than raising
            // it. Plain JS text compresses far better over the wire than the old binary .glb did,
            // so this budget is deliberately tighter than the old model-file one was.
            const long budgetBytes = 200 * 1024;
            Assert.True(script.Length < budgetBytes,
                $"car-body.js is {script.Length / 1024}KB, over the {budgetBytes / 1024}KB budget for a phone on mobile data.");
        }
    }
}
