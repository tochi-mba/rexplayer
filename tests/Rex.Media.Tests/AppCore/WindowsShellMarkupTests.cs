using System.Xml.Linq;
using Rex.Media.AppCore.Design;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>The Windows window is what <see cref="ShellScreens"/> says it is, read straight from its markup.</summary>
public sealed class WindowsShellMarkupTests
{
    private const string Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Ui = Presentation;
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string AutomationId = "AutomationProperties.AutomationId";

    // Controls in the bar that answer to a feature row of their own, not to the look of the bar.
    private static readonly string[] OwnFeatureRows = ["ListenButton"];

    private static readonly string[] ControlElements = ["Button", "ToggleButton", "DropDownButton", "Slider", "TextBlock"];

    private static readonly Lazy<XDocument> Window = new(() => XDocument.Load(RepoPaths.Combine("src", "Rex.Media.App", "MainWindow.xaml")));

    public static TheoryData<string> Screens() => [.. ShellScreens.All.Select(screen => screen.Name)];

    [Theory]
    [MemberData(nameof(Screens))]
    public void EveryDescribedControlIsInTheWindowOnce(string name)
    {
        foreach (var control in Screen(name).Controls)
        {
            Assert.True(Matches(control.Id).Count == 1, $"{control.Id} is in MainWindow.xaml {Matches(control.Id).Count} times");
        }
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void TheControlsComeInTheOrderTheScreenListsThem(string name)
    {
        var order = Window.Value.Descendants().Select((element, index) => (element, index)).ToDictionary(pair => pair.element, pair => pair.index);
        var positions = Screen(name).Controls.Select(control => order[Find(control.Id)]).ToList();
        Assert.Equal([.. positions.Order()], positions);
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void NoDescribedControlIsHiddenInTheMarkup(string name)
    {
        foreach (var control in Screen(name).Controls)
        {
            Assert.True((string?)Find(control.Id).Attribute("Visibility") != "Collapsed", $"{control.Id} is collapsed");
        }
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void EveryDescribedControlIsDrawnAsItsKind(string name)
    {
        foreach (var control in Screen(name).Controls)
        {
            var drawn = KindOf(Find(control.Id));
            Assert.True(drawn == control.Kind, $"{control.Id} is described as {ShellScreens.Spell(control.Kind)} but its markup is {(drawn is { } kind ? ShellScreens.Spell(kind) : "no kind")}");
        }
    }

    // Fails when a button is added to the Windows bar without describing it, so the Mac shell is
    // told about it by a failing test rather than by a person noticing.
    [Fact]
    public void EveryControlAlwaysShownInTheBarIsDescribed()
    {
        var described = ShellScreens.Transport.Controls.Select(control => control.Id).ToHashSet(StringComparer.Ordinal);
        var shown = new[] { "SeekRow", "TransportButtons", "AudioControlStrip" }
            .SelectMany(panel => AlwaysShownControls(Find(panel)))
            .ToList();

        Assert.NotEmpty(shown);
        Assert.All(shown, id => Assert.True(described.Contains(id) || OwnFeatureRows.Contains(id), $"{id} is always shown in the bar but ShellScreens.Transport does not describe it"));
        Assert.All(OwnFeatureRows, id => Assert.Contains(id, shown));
    }

    [Theory]
    [InlineData("<Slider />", "slider")]
    [InlineData("<TextBlock Text=\"--:--\" />", "label")]
    [InlineData("<ToggleButton><FontIcon /></ToggleButton>", "icon-toggle")]
    [InlineData("<ToggleButton Content=\"On\" />", null)]
    [InlineData("<Button Style=\"{StaticResource AccentButtonStyle}\"><FontIcon /></Button>", "accent-icon")]
    [InlineData("<Button Style=\"{StaticResource AccentButtonStyle}\" Content=\"Open\" />", "accent-text")]
    [InlineData("<Button Style=\"{StaticResource AccentButtonStyle}\" />", null)]
    [InlineData("<Button Content=\"Browse\"><Button.Resources><StaticResource x:Key=\"ButtonBackground\" ResourceKey=\"RexOverlayFillBrush\" /></Button.Resources></Button>", "overlay-text")]
    [InlineData("<Button><FontIcon /></Button>", "icon")]
    [InlineData("<DropDownButton><FontIcon /></DropDownButton>", "icon")]
    [InlineData("<Button Content=\"1×\" />", "text")]
    [InlineData("<DropDownButton Content=\"Later\" />", "text")]
    [InlineData("<Button>Start over</Button>", "text")]
    [InlineData("<Button Content=\"Both\"><FontIcon /></Button>", "text")]
    [InlineData("<Button><Grid /></Button>", null)]
    [InlineData("<Canvas />", null)]
    public void TheKindIsReadFromTheMarkupAsTheShellDrawsIt(string markup, string? spelling)
    {
        var element = XElement.Parse($"<Root xmlns=\"{Presentation}\" xmlns:x=\"{Xaml.NamespaceName}\">{markup}</Root>").Elements().Single();
        Assert.Equal(spelling, KindOf(element) is { } kind ? ShellScreens.Spell(kind) : null);
    }

    private static ShellScreen Screen(string name) => ShellScreens.All.Single(screen => screen.Name == name);

    // The automation id names a control; x:Name stands in for the few, like the tagline, that have none.
    private static List<XElement> Matches(string id)
    {
        var byAutomation = Window.Value.Descendants().Where(e => (string?)e.Attribute(AutomationId) == id).ToList();
        return byAutomation.Count > 0 ? byAutomation : [.. Window.Value.Descendants().Where(e => (string?)e.Attribute(Xaml + "Name") == id)];
    }

    private static XElement Find(string id) => Matches(id).Single();

    // A control is always shown when neither it nor anything between it and the panel is collapsed,
    // and it is not inside a flyout or other property, which opens only on demand.
    private static IEnumerable<string> AlwaysShownControls(XElement panel) => panel.Descendants()
        .Where(e => e.Name.Namespace == Ui && ControlElements.Contains(e.Name.LocalName) && e.Attribute(AutomationId) is not null)
        .Where(e => e.AncestorsAndSelf().TakeWhile(a => a != panel).All(a => !a.Name.LocalName.Contains('.', StringComparison.Ordinal) && (string?)a.Attribute("Visibility") != "Collapsed"))
        .Select(e => e.Attribute(AutomationId)!.Value);

    // The kind a control is drawn as, from what its markup says; null when it is none of them.
    private static ShellControlKind? KindOf(XElement element)
    {
        var icon = element.Elements(Ui + "FontIcon").Any();
        var words = element.Attribute("Content") is not null || element.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value));
        var accent = (string?)element.Attribute("Style") == "{StaticResource AccentButtonStyle}";
        var overlay = element.Elements(Ui + "Button.Resources").Descendants().Any(e => (string?)e.Attribute("ResourceKey") == "RexOverlayFillBrush");

        return element.Name.LocalName switch
        {
            "Slider" => ShellControlKind.Slider,
            "TextBlock" => ShellControlKind.Label,
            "ToggleButton" when icon => ShellControlKind.IconToggle,
            "Button" when accent && icon => ShellControlKind.AccentIcon,
            "Button" when accent && words => ShellControlKind.AccentText,
            "Button" when accent => null,
            "Button" when overlay => ShellControlKind.OverlayText,
            "Button" or "DropDownButton" when icon && !words => ShellControlKind.Icon,
            "Button" or "DropDownButton" when words => ShellControlKind.Text,
            _ => null,
        };
    }
}
