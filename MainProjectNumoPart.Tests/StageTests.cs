using System;
using System.Collections.Generic;
using System.Linq;
using MainProjectNumoPart.Models;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class StageTests
    {
        // EF Core stores this enum as int, so these numbers ARE the database values (same
        // concern as PartTests.NumericValuesNeverChange). New stages must be appended after the
        // existing ones, never inserted in the middle, or every existing photo silently
        // re-labels itself to a different stage.
        [Fact]
        public void NumericValuesNeverChange()
        {
            var expected = new Dictionary<Stage, int>
            {
                [Stage.Checkin] = 0,
                [Stage.Quote] = 1,
                [Stage.Progress] = 2,
                [Stage.Checkout] = 3,
                [Stage.Extra] = 4,
                [Stage.WheelAlignment] = 5,
                [Stage.Diagnostics] = 6
            };

            foreach (var (stage, value) in expected)
            {
                Assert.Equal(value, (int)stage);
            }

            // Also catches a new member being added without being pinned above.
            Assert.Equal(expected.Count, Enum.GetValues<Stage>().Length);
        }

        [Fact]
        public void NoTwoStagesShareAValue()
        {
            var values = Enum.GetValues<Stage>().Select(s => (int)s).ToList();
            Assert.Equal(values.Count, values.Distinct().Count());
        }
    }
}
