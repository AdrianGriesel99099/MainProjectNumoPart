using System.Linq;
using MainProjectNumoPart.Services;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class PhotoExportTextWrapTests
    {
        // A stand-in for XGraphics.MeasureString: character count as "width" keeps the wrap
        // logic itself under test without pulling in real font metrics.
        private static double MeasureByLength(string s) => s.Length;

        [Fact]
        public void ShortText_FitsOnOneLine()
        {
            var lines = PhotoExportTextWrap.Wrap("hello world", MeasureByLength, maxWidth: 40);

            Assert.Single(lines);
            Assert.Equal("hello world", lines[0]);
        }

        [Fact]
        public void LongText_WrapsAtWordBoundaries()
        {
            var lines = PhotoExportTextWrap.Wrap("one two three four five", MeasureByLength, maxWidth: 10);

            Assert.All(lines, l => Assert.True(l.Length <= 10, $"line '{l}' exceeds max width"));
            // Re-joining the wrapped lines must reproduce every original word, in order — wrapping
            // must never drop or reorder text, only insert breaks.
            Assert.Equal("one two three four five", string.Join(" ", lines));
        }

        [Fact]
        public void ExplicitNewlines_StartNewParagraphs()
        {
            var lines = PhotoExportTextWrap.Wrap("first line\nsecond line", MeasureByLength, maxWidth: 40);

            Assert.Equal(new[] { "first line", "second line" }, lines);
        }

        [Fact]
        public void EmptyString_ReturnsOneBlankLine()
        {
            var lines = PhotoExportTextWrap.Wrap("", MeasureByLength, maxWidth: 40);

            Assert.Single(lines);
            Assert.Equal("", lines[0]);
        }

        [Fact]
        public void SingleWordLongerThanMaxWidth_KeptWholeRatherThanDropped()
        {
            var lines = PhotoExportTextWrap.Wrap("supercalifragilisticexpialidocious", MeasureByLength, maxWidth: 5);

            Assert.Single(lines);
            Assert.Equal("supercalifragilisticexpialidocious", lines[0]);
        }
    }
}
