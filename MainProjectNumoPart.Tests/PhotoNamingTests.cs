using System;
using MainProjectNumoPart.Services;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class PhotoNamingTests
    {
        [Fact]
        public void ResolveBlobFolderName_PrefersVinOverReg()
        {
            Assert.Equal("1HGBH41JXMN109186", PhotoNaming.ResolveBlobFolderName("1HGBH41JXMN109186", "CA481329"));
        }

        [Fact]
        public void ResolveBlobFolderName_FallsBackToReg()
        {
            Assert.Equal("CA481329", PhotoNaming.ResolveBlobFolderName(null, "CA481329"));
        }

        [Fact]
        public void ResolveBlobFolderName_ThrowsWhenNeitherProvided()
        {
            Assert.Throws<ArgumentException>(() => PhotoNaming.ResolveBlobFolderName(null, null));
        }

        [Fact]
        public void ResolveBlobFolderName_ThrowsWhenBothBlank()
        {
            Assert.Throws<ArgumentException>(() => PhotoNaming.ResolveBlobFolderName("  ", ""));
        }

        [Fact]
        public void BuildFileName_VinAndReg()
        {
            var name = PhotoNaming.BuildFileName("1HGBH41JXMN109186", "CA481329", 1, ".jpg");
            Assert.Equal("1HGBH41JXMN109186-VIN-CA481329-Reg-001.jpg", name);
        }

        [Fact]
        public void BuildFileName_VinOnly()
        {
            var name = PhotoNaming.BuildFileName("1HGBH41JXMN109186", null, 1, ".jpg");
            Assert.Equal("1HGBH41JXMN109186-VIN-001.jpg", name);
        }

        [Fact]
        public void BuildFileName_RegOnly()
        {
            var name = PhotoNaming.BuildFileName(null, "CA481329", 1, ".jpg");
            Assert.Equal("CA481329-Reg-001.jpg", name);
        }

        [Fact]
        public void BuildFileName_PadsSequenceToThreeDigits()
        {
            var name = PhotoNaming.BuildFileName("VIN1", null, 42, ".png");
            Assert.Equal("VIN1-VIN-042.png", name);
        }

        [Fact]
        public void BuildFileName_ThrowsWhenNeitherProvided()
        {
            Assert.Throws<ArgumentException>(() => PhotoNaming.BuildFileName(null, null, 1, ".jpg"));
        }
    }
}
