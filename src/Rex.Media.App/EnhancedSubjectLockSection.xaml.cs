using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Rex.Media.Vision;

namespace Rex.Media.App;

/// <summary>
/// The optional model download in Preferences (ADR-0019). It shows the state of a
/// <see cref="ModelSet"/> that outlives the dialog, so a download keeps going after Preferences
/// closes and the section picks it up again when it reopens.
/// </summary>
public sealed partial class EnhancedSubjectLockSection : UserControl
{
    private readonly ModelSet _models;
    private readonly Action _download;
    private readonly Action _stop;
    private readonly Action _remove;

    public EnhancedSubjectLockSection(ModelSet models, Action download, Action stop, Action remove)
    {
        _models = models;
        _download = download;
        _stop = stop;
        _remove = remove;
        InitializeComponent();
        Note.Text = ModelDownloadText.Note;
        foreach (var line in ModelDownloadText.Contents(models.Models))
        {
            Contents.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap, Opacity = 0.85 });
        }

        Total.Text = ModelDownloadText.Total(models.Models);
        _models.Changed += OnChanged;
        Unloaded += (_, _) => _models.Changed -= OnChanged;
        Show();
    }

    private void OnChanged() => DispatcherQueue.TryEnqueue(Show);

    private void Show()
    {
        var view = _models.View;
        Status.Text = view.Status;
        Status.Foreground = view.IsError && Application.Current.Resources.TryGetValue("SystemFillColorCriticalBrush", out var brush) && brush is Brush critical
            ? critical
            : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
        Download.Content = view.DownloadLabel;
        Download.IsEnabled = view.CanDownload;
        Stop.Visibility = view.CanCancel ? Visibility.Visible : Visibility.Collapsed;
        Remove.IsEnabled = view.CanRemove;
        Progress.Visibility = view.ShowProgress ? Visibility.Visible : Visibility.Collapsed;
        Progress.Value = view.ProgressPercent;
    }

    private void OnDownload(object sender, RoutedEventArgs e) => _download();

    private void OnStop(object sender, RoutedEventArgs e) => _stop();

    private void OnRemove(object sender, RoutedEventArgs e) => _remove();
}
