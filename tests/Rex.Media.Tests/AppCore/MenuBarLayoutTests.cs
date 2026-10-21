using Rex.Media.AppCore.Commands;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>The menu bar every shell builds from the same data (UI-02): every entry is real, nothing is offered twice, and the groups read cleanly.</summary>
public sealed class MenuBarLayoutTests
{
    private static readonly string[] MenuTitles = ["Media", "Playback", "Audio", "Subtitles", "Video", "View", "Help"];

    [Fact]
    [Capability("UI-02")]
    public void TheMenusComeInTheOrderPeopleExpect()
    {
        Assert.Equal(MenuTitles, MenuBarLayout.Menus.Select(menu => menu.Title));
    }

    [Fact]
    public void EveryEntryIsACommandOrAKnownSubMenu()
    {
        var entries = MenuBarLayout.Menus.SelectMany(menu => menu.Commands).Concat(MenuBarLayout.SubMenus.Values.SelectMany(group => group.Commands)).OfType<string>();
        foreach (var entry in entries)
        {
            if (MenuBarLayout.IsSubMenu(entry))
            {
                Assert.True(MenuBarLayout.SubMenus.ContainsKey(entry), entry + " opens no sub-menu.");
            }
            else
            {
                Assert.True(CommandCatalog.Find(entry) is not null, entry + " is not a command.");
            }
        }
    }

    [Fact]
    public void EverySubMenuIsOpenedFromExactlyOneMenuAndNeverNestsAnother()
    {
        var opened = MenuBarLayout.Menus.SelectMany(menu => menu.Commands).OfType<string>().Where(MenuBarLayout.IsSubMenu).ToList();
        Assert.Equal(opened.Order(StringComparer.Ordinal), MenuBarLayout.SubMenus.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(opened.Count, opened.Distinct(StringComparer.Ordinal).Count());
        Assert.All(MenuBarLayout.SubMenus.Values, group => Assert.DoesNotContain(group.Commands.OfType<string>(), MenuBarLayout.IsSubMenu));
        Assert.Equal("jump", MenuBarLayout.SubMenuId(MenuBarLayout.SubMenuPrefix + "jump"));
    }

    [Fact]
    public void NoCommandIsOfferedTwiceInTheBar()
    {
        var all = MenuBarLayout.AllCommands().ToList();
        Assert.Equal(all.Count, all.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(CommandCatalog.JumpForwardLong, all);
        Assert.Contains(CommandCatalog.Quit, all);
    }

    [Fact]
    public void SeparatorsNeverLeadFollowOrTouchEachOther()
    {
        var lists = MenuBarLayout.Menus.Select(menu => menu.Commands).Concat(MenuBarLayout.SubMenus.Values.Select(group => group.Commands));
        foreach (var commands in lists)
        {
            Assert.NotNull(commands[0]);
            Assert.NotNull(commands[^1]);
            for (var i = 1; i < commands.Length; i++)
            {
                Assert.False(commands[i] is null && commands[i - 1] is null, "Two separators in a row.");
            }
        }
    }

    [Fact]
    public void OnOffCommandsAreRealAndOfferedWhereTheTickCanBeSeen()
    {
        var onOff = CommandCatalog.All.Select(command => command.Id).Where(MenuBarLayout.IsOnOff).ToList();
        Assert.Contains(CommandCatalog.ToggleFullScreen, onOff);
        Assert.Contains(CommandCatalog.MinimalInterface, onOff);
        Assert.DoesNotContain(CommandCatalog.PlayPause, onOff);
        Assert.All(onOff, command => Assert.Contains(command, MenuBarLayout.AllCommands()));
    }
}
