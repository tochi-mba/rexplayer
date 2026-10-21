using Rex.Media.AppCore.Library;

namespace Rex.Media.Tests.AppCore;

/// <summary>How show names are told apart: by their words, and a lone episode by the show its name extends.</summary>
public sealed class ShowNamesTests
{
    [Theory]
    [InlineData("Futurama", "futurama")]
    [InlineData("Pokémon", "pokemon")]
    [InlineData("POKEMON.", "pokemon")]
    [InlineData("Marvel's Agents", "marvels agents")]
    [InlineData("Law & Order", "law and order")]
    [InlineData("Law+Order", "law and order")]
    [InlineData("  The   X-Files ", "the x files")]
    [InlineData("Doctor Who: Confidential", "doctor who confidential")]
    [InlineData("!!!", "!!!")]
    public void AKeyIsTheNamesWordsWithoutCaseAccentsOrPunctuation(string name, string key)
    {
        Assert.Equal(key, ShowNames.Key(name));
    }

    [Theory]
    [InlineData("futurama s07x01 simpsorama", "futurama", true)]
    [InlineData("futurama s07", "futurama", true)]
    [InlineData("futurama s07e01e02", "futurama", true)]
    [InlineData("futurama 7x01 simpsorama", "futurama", true)]
    [InlineData("futurama season 7 bonus", "futurama", true)]
    [InlineData("futurama special", "futurama", true)]
    [InlineData("futurama sp01", "futurama", true)]
    [InlineData("futurama ova", "futurama", true)]
    [InlineData("futurama extras", "futurama", true)]
    [InlineData("futurama e05", "futurama", true)]
    [InlineData("doctor who confidential", "doctor who", false)]
    [InlineData("doctor whom s01", "doctor who", false)]
    [InlineData("apollo 13", "apollo", false)]
    [InlineData("spider man 2", "spider man", false)]
    [InlineData("futurama", "futurama", false)]
    [InlineData("futurama s07", "", false)]
    [InlineData("futurama s07", "simpsons", false)]
    public void ALoneEpisodeExtendsAShowOnlyWithAMarkAfterItsName(string candidate, string host, bool extends)
    {
        Assert.Equal(extends, ShowNames.Extends(candidate, host));
    }

    [Fact]
    public void NamesAreRequiredAndABrokenCharacterIsNoWord()
    {
        Assert.Equal("show", ShowNames.Key("\ud800 Show"));
        Assert.Throws<ArgumentNullException>(() => ShowNames.Key(null!));
        Assert.Throws<ArgumentNullException>(() => ShowNames.Extends(null!, "a"));
        Assert.Throws<ArgumentNullException>(() => ShowNames.Extends("a", null!));
    }
}
