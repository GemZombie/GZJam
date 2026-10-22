using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace GZJam.App.Views;

/// <summary>Settings › About.</summary>
public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();
        VersionText.Text = About.Version;
        MakerText.Text = $"A DJ app for swing dancing, made by {About.Maker}.";
        ProjectLink.Content = About.ProjectUrl.Replace("https://", "");
        BuiltWithText.Text = About.BuiltWith();
        NoticesButton.IsVisible = File.Exists(About.NoticesPath);
    }

    private async void OnProjectClick(object? sender, RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(new Uri(About.ProjectUrl));

    /// <summary>Opens the notices in the computer's own text viewer: they're long.</summary>
    private async void OnNoticesClick(object? sender, RoutedEventArgs e) =>
        await Launcher.LaunchFileInfoAsync(new FileInfo(About.NoticesPath));

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
