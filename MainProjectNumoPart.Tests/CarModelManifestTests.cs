using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MainProjectNumoPart.Models;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    // The 3D picker binds clicks to mesh names in car.glb, and those names are supposed to match
    // Part enum members exactly. Nothing in the compiler enforces that: rename an enum member
    // without regenerating the model and you get a panel that can never be selected — invisible
    // until someone tries to tag that exact part, possibly months later during a dispute.
    // These tests are the only thing standing between that rename and a silent hole in the UI.
    public class CarModelManifestTests
    {
        private static readonly string[] Manifest = LoadManifest();

        private static string RepoRoot()
        {
            // Walk up from the test binary until the repo root (the folder holding wwwroot).
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "wwwroot")))
            {
                dir = dir.Parent;
            }
            return dir?.FullName
                ?? throw new InvalidOperationException("Could not locate the repo root from the test output directory.");
        }

        private static string[] LoadManifest()
        {
            var path = Path.Combine(RepoRoot(), "wwwroot", "models", "car-parts.json");
            if (!File.Exists(path))
            {
                throw new InvalidOperationException(
                    $"car-parts.json not found at {path}. Regenerate it with: " +
                    "blender --background --python tools/car-model/build_car.py");
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.GetProperty("meshes").EnumerateArray()
                .Select(e => e.GetString()!)
                .ToArray();
        }

        [Fact]
        public void EveryMeshNameIsARealPart()
        {
            var unknown = Manifest.Where(m => !Enum.TryParse<Part>(m, out _)).ToList();

            Assert.True(unknown.Count == 0,
                "car.glb contains meshes that are not Part values: " + string.Join(", ", unknown) +
                ". Either the enum member was renamed or the Blender script is out of date.");
        }

        [Fact]
        public void EveryPartExistsInTheModel()
        {
            var present = new HashSet<string>(Manifest, StringComparer.Ordinal);
            var missing = Enum.GetNames<Part>().Where(n => !present.Contains(n)).ToList();

            Assert.True(missing.Count == 0,
                "These parts have no mesh in car.glb, so they can never be picked in 3D: " +
                string.Join(", ", missing) +
                ". Regenerate with: blender --background --python tools/car-model/build_car.py");
        }

        [Fact]
        public void ModelFileIsPresentAndSmallEnoughForAPhone()
        {
            var glb = new FileInfo(Path.Combine(RepoRoot(), "wwwroot", "models", "car.glb"));

            Assert.True(glb.Exists, $"car.glb is missing at {glb.FullName}.");

            // The picker sits on the upload page, which staff open on mobile data in a workshop.
            // A model that takes seconds to arrive delays the form behind it, so this is a real
            // budget rather than tidiness — if it trips, simplify the model rather than raising it.
            const long budgetBytes = 2 * 1024 * 1024;
            Assert.True(glb.Length < budgetBytes,
                $"car.glb is {glb.Length / 1024}KB, over the {budgetBytes / 1024}KB budget for a phone on mobile data.");
        }
    }
}
