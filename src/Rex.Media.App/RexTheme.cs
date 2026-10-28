using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Rex.Media.AppCore.Design;

namespace Rex.Media.App;

/// <summary>The REX tokens for code that builds its own elements: App.xaml's brushes and <see cref="DesignTokens"/> colours.</summary>
internal static class RexTheme
{
    /// <summary>A brush from App.xaml, such as <c>RexOverlayTextBrush</c>.</summary>
    public static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    /// <summary>A <see cref="DesignTokens"/> colour, 0xAARRGGBB, as a XAML colour.</summary>
    public static Windows.UI.Color Color(uint argb) =>
        Windows.UI.Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
}
