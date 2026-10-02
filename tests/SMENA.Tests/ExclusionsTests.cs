using System;
using SMENA.Services;
using Xunit;

namespace SMENA.Tests
{
    public class ExclusionsTests
    {
        [Fact]
        public void Parse_Trims_And_Skips_Empty_Lines()
        {
            var parsed = Exclusions.Parse("  bank \r\n\r\npassword\n\n");
            Assert.Equal(new[] { "bank", "password" }, parsed);
        }

        [Fact]
        public void Parse_Null_Or_Empty_Gives_No_Patterns()
        {
            Assert.Empty(Exclusions.Parse(null));
            Assert.Empty(Exclusions.Parse(""));
        }

        [Fact]
        public void IsMatch_Title_Hit_Is_Case_Insensitive()
        {
            Assert.True(Exclusions.IsMatch("bank", "chrome", "My Bank — Home"));
        }

        [Fact]
        public void IsMatch_Process_Hit()
        {
            Assert.True(Exclusions.IsMatch("chrome", "chrome", "anything"));
            Assert.True(Exclusions.IsMatch("incognito", "chrome", "Incognito tab"));
            Assert.True(Exclusions.IsMatch("1password", "1password", "1Password"));
        }

        [Fact]
        public void IsMatch_Miss_Returns_False()
        {
            Assert.False(Exclusions.IsMatch("bank", "blender", "t34_hull.blend — Blender"));
            Assert.False(Exclusions.IsMatch(null, "blender", "anything"));
        }

        [Fact]
        public void IsMatch_Substring_Not_Word_Match_Matches_Inside_Words()
        {
            // документированная семантика: подстрока, «banking» матчится по «bank»
            Assert.True(Exclusions.IsMatch("bank", "app", "banking schedule"));
        }
    }

    public class UpdateCheckerTests
    {
        [Theory]
        [InlineData("v0.24", "0.23.0", true)]
        [InlineData("0.24.1", "0.24.0", true)]
        [InlineData("v0.22", "0.23.0", false)]
        [InlineData("v0.23", "0.23.0.0", false)]
        [InlineData("v0.23.1", "0.23.0.0", true)]
        [InlineData("garbage", "0.23.0", false)]
        [InlineData("", "0.23.0", false)]
        public void IsNewer_Compares_Tag_Against_Current(string tag, string current, bool expected)
        {
            Assert.Equal(expected, UpdateChecker.IsNewer(tag, Version.Parse(current)));
        }

        [Fact]
        public void CurrentVersion_Is_Not_Zero()
        {
            Assert.True(UpdateChecker.CurrentVersion() > new Version(0, 0));
        }
    }
}
