using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Rex.Media.AppCore.Automation;
using Rex.Media.AppCore.Commands;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Rex.Media.App;

/// <summary>
/// The window as a script drives it (automation pipe protocol 2): every control with an automation
/// id, the menu bar as built, and the same press, toggle, set, key, text and snapshot actions the
/// macOS window answers. What the pipe accepts and how it fails is decided in
/// <see cref="ShellAutomation"/>; this only finds WinUI's widgets and works them through their
/// automation peers, as a screen reader would.
/// </summary>
public sealed partial class MainWindow : IShellSurface
{
    /// <summary>Answers a <c>ui.</c> request on the window's thread.</summary>
    private async Task AnswerUiAsync(AutomationRequest request, TaskCompletionSource<AutomationReply> answered)
    {
        try
        {
            answered.SetResult(await ShellAutomation.AnswerAsync(request, this));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            answered.SetResult(AutomationReply.Failed("The window could not do that: " + ex.Message));
        }
    }

    IReadOnlyList<UiControl> IShellSurface.Controls() =>
        [.. Everything().Where(element => AutomationProperties.GetAutomationId(element) is { Length: > 0 }).Select(Describe)];

    IReadOnlyList<UiMenuItem> IShellSurface.Menus() =>
        [.. Menu.Items.Select(menu => new UiMenuItem
        {
            Id = AutomationProperties.GetAutomationId(menu),
            Title = menu.Title,
            Kind = "menu",
            Items = Describe(menu.Items),
        })];

    bool IShellSurface.Press(string id)
    {
        if (Peer(id) is not { } peer)
        {
            return false;
        }

        switch (peer.GetPattern(PatternInterface.Invoke), peer.GetPattern(PatternInterface.SelectionItem), peer.GetPattern(PatternInterface.Toggle))
        {
            case (IInvokeProvider invoke, _, _):
                invoke.Invoke();
                return true;
            case (_, ISelectionItemProvider select, _):
                select.Select();
                return true;
            case (_, _, IToggleProvider toggle):
                toggle.Toggle();
                return true;
            default:
                return false;
        }
    }

    bool IShellSurface.Toggle(string id)
    {
        if (Peer(id)?.GetPattern(PatternInterface.Toggle) is not IToggleProvider toggle)
        {
            return false;
        }

        toggle.Toggle();
        return true;
    }

    bool IShellSurface.SetValue(string id, double value)
    {
        if (Peer(id)?.GetPattern(PatternInterface.RangeValue) is not IRangeValueProvider range)
        {
            return false;
        }

        range.SetValue(value);
        return true;
    }

    bool IShellSurface.SendChord(KeyChord chord)
    {
        if (_keymap.CommandFor(chord) is not { } command)
        {
            return false;
        }

        Run(command);
        return true;
    }

    string? IShellSurface.Text(string id) => Find(id) is { } element ? TextOf(element) ?? AutomationProperties.GetName(element) : null;

    async Task IShellSurface.SnapshotAsync(string path)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(Root);
        var pixels = (await bitmap.GetPixelsAsync()).ToArray();
        using var memory = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, memory);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
        await encoder.FlushAsync();
        memory.Seek(0);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await using var file = File.Create(path);
        await memory.AsStreamForRead().CopyToAsync(file);
    }

    /// <summary>Every element of the window and of any open dialog or flyout, in tree order.</summary>
    private IEnumerable<FrameworkElement> Everything()
    {
        var roots = new List<DependencyObject> { Root };
        roots.AddRange(VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot).Select(popup => (DependencyObject)popup.Child).Where(child => child is not null));
        var pending = new Stack<DependencyObject>(Enumerable.Reverse(roots));
        while (pending.Count > 0)
        {
            var next = pending.Pop();
            if (next is FrameworkElement element)
            {
                yield return element;
            }

            for (var i = VisualTreeHelper.GetChildrenCount(next) - 1; i >= 0; i--)
            {
                pending.Push(VisualTreeHelper.GetChild(next, i));
            }
        }
    }

    /// <summary>The control or menu entry with this automation id, wherever it is.</summary>
    private FrameworkElement? Find(string id) =>
        Everything().FirstOrDefault(element => AutomationProperties.GetAutomationId(element) == id)
        ?? MenuEntries(Menu.Items.Cast<FrameworkElement>()).FirstOrDefault(element => AutomationProperties.GetAutomationId(element) == id);

    private AutomationPeer? Peer(string id) => Find(id) is { } element ? FrameworkElementAutomationPeer.CreatePeerForElement(element) : null;

    /// <summary>The menu bar's entries at every depth, shown or not: flyout items are not in the visual tree until opened.</summary>
    private static IEnumerable<FrameworkElement> MenuEntries(IEnumerable<FrameworkElement> level)
    {
        foreach (var entry in level)
        {
            yield return entry;
            var children = entry switch
            {
                MenuBarItem menu => menu.Items,
                MenuFlyoutSubItem sub => sub.Items,
                _ => null,
            };
            if (children is not null)
            {
                foreach (var child in MenuEntries(children))
                {
                    yield return child;
                }
            }
        }
    }

    private static UiControl Describe(FrameworkElement element) => new()
    {
        Id = AutomationProperties.GetAutomationId(element),
        Kind = element.GetType().Name,
        Name = AutomationProperties.GetName(element),
        Enabled = element is not Control control || control.IsEnabled,
        Visible = element.Visibility == Visibility.Visible,
        Checked = element is ToggleButton toggle ? toggle.IsChecked : null,
        Value = element is RangeBase range ? range.Value : null,
        Text = TextOf(element),
    };

    private static string? TextOf(FrameworkElement element) => element switch
    {
        TextBlock text => text.Text,
        TextBox box => box.Text,
        RichTextBlock rich => string.Concat(rich.Blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Inlines.OfType<Run>()).Select(run => run.Text)),
        ContentControl { Content: string content } => content,
        _ => null,
    };

    private static IReadOnlyList<UiMenuItem> Describe(IList<MenuFlyoutItemBase> items) =>
        [.. items.Select(item => item switch
        {
            MenuFlyoutSeparator => new UiMenuItem { Id = "", Kind = "separator" },
            MenuFlyoutSubItem sub => new UiMenuItem { Id = AutomationProperties.GetAutomationId(sub), Title = sub.Text, Kind = "submenu", Enabled = sub.IsEnabled, Items = Describe(sub.Items) },
            ToggleMenuFlyoutItem toggle => Entry(toggle, "toggle", toggle.IsChecked),
            RadioMenuFlyoutItem radio => Entry(radio, "toggle", radio.IsChecked),
            MenuFlyoutItem command => Entry(command, "command", null),
            _ => new UiMenuItem { Id = AutomationProperties.GetAutomationId(item), Kind = item.GetType().Name },
        })];

    private static UiMenuItem Entry(MenuFlyoutItem item, string kind, bool? isChecked) => new()
    {
        Id = AutomationProperties.GetAutomationId(item),
        Title = item.Text,
        Kind = kind,
        Shortcut = string.IsNullOrEmpty(item.KeyboardAcceleratorTextOverride) ? null : item.KeyboardAcceleratorTextOverride,
        Checked = isChecked,
        Enabled = item.IsEnabled,
    };
}
