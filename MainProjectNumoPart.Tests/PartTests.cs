using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using MainProjectNumoPart.Models;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class PartTests
    {
        // EF Core stores enums as int, so these numbers ARE the database values. If someone
        // inserts a member in the middle or reorders the enum, every existing photo silently
        // re-labels itself to a different panel — and nobody notices until a dispute turns on
        // which part was photographed. This test is the tripwire: changing an existing number
        // fails here, loudly, at build time.
        [Fact]
        public void NumericValuesNeverChange()
        {
            var expected = new Dictionary<Part, int>
            {
                [Part.FrontBumper] = 1,
                [Part.Bonnet] = 2,
                [Part.HeadlightLeft] = 3,
                [Part.HeadlightRight] = 4,
                [Part.Windscreen] = 5,
                [Part.Roof] = 6,
                [Part.RearWindscreen] = 7,
                [Part.BootTailgate] = 8,
                [Part.RearBumper] = 9,
                [Part.TaillightLeft] = 10,
                [Part.TaillightRight] = 11,
                [Part.WingFrontLeft] = 12,
                [Part.WingFrontRight] = 13,
                [Part.DoorFrontLeft] = 14,
                [Part.DoorFrontRight] = 15,
                [Part.DoorRearLeft] = 16,
                [Part.DoorRearRight] = 17,
                [Part.QuarterRearLeft] = 18,
                [Part.QuarterRearRight] = 19,
                [Part.MirrorLeft] = 20,
                [Part.MirrorRight] = 21,
                [Part.SillLeft] = 22,
                [Part.SillRight] = 23,
                [Part.WheelFrontLeft] = 24,
                [Part.WheelFrontRight] = 25,
                [Part.WheelRearLeft] = 26,
                [Part.WheelRearRight] = 27,
                [Part.Interior] = 28,
                [Part.EngineBay] = 29,
                [Part.LoadArea] = 30,
                [Part.Odometer] = 31,
                [Part.Undercarriage] = 32,
                [Part.Other] = 33
            };

            foreach (var (part, value) in expected)
            {
                Assert.Equal(value, (int)part);
            }

            // Also catches a NEW member being added without being pinned above — otherwise a
            // part could be introduced with a colliding or accidental value and go unnoticed.
            Assert.Equal(expected.Count, Enum.GetValues<Part>().Length);
        }

        [Fact]
        public void NoTwoPartsShareAValue()
        {
            var values = Enum.GetValues<Part>().Select(p => (int)p).ToList();
            Assert.Equal(values.Count, values.Distinct().Count());
        }

        // Html.GetEnumSelectList<Part>() renders these, so a missing one shows a raw member
        // name like "QuarterRearLeft" to a user instead of "Left rear quarter".
        [Fact]
        public void EveryPartHasAFriendlyDisplayName()
        {
            foreach (var part in Enum.GetValues<Part>())
            {
                var member = typeof(Part).GetMember(part.ToString())[0];
                var display = member.GetCustomAttributes(typeof(DisplayAttribute), false)
                    .Cast<DisplayAttribute>()
                    .SingleOrDefault();

                Assert.True(display is not null, $"{part} has no [Display] attribute.");
                Assert.False(string.IsNullOrWhiteSpace(display!.Name), $"{part} has an empty [Display] name.");

                // Single-word parts (Bonnet, Roof) legitimately have a display name identical to
                // the member name. Only multi-word PascalCase names indicate a forgotten label —
                // "QuarterRearLeft" leaking to a user is the failure this catches.
                var isMultiWord = part.ToString().Count(char.IsUpper) > 1;
                if (isMultiWord)
                {
                    Assert.True(display.Name != part.ToString(),
                        $"{part} still shows its raw member name; give it a readable [Display] name.");
                }
            }
        }

        [Fact]
        public void DisplayNamesAreUnique()
        {
            var names = Enum.GetValues<Part>()
                .Select(p => typeof(Part).GetMember(p.ToString())[0]
                    .GetCustomAttributes(typeof(DisplayAttribute), false)
                    .Cast<DisplayAttribute>().Single().Name)
                .ToList();

            Assert.Equal(names.Count, names.Distinct().Count());
        }
    }
}
