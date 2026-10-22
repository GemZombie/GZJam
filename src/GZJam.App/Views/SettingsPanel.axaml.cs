using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace GZJam.App.Views;

/// <summary>The sections down the side of Settings.</summary>
public enum SettingsSection
{
    Sound,
    Decks,
    Screen,
    Library,
    About,
}

/// <summary>
/// Settings, shown over the dimmed main screen (see <see cref="MainWindow.OpenSettings"/>). Music
/// keeps playing while it's open.
/// </summary>
public partial class SettingsPanel : UserControl
{
    private bool _aboutFilled;

    public SettingsPanel()
    {
        InitializeComponent();
        NavVersionText.Text = $"GZJam {About.Version}";
        foreach ((RadioButton nav, _) in Sections)
            nav.IsCheckedChanged += (_, _) => ShowCheckedSection();
    }

    /// <summary>DONE was pressed.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Library › IMPORT… was pressed.</summary>
    public event EventHandler? MixxxImportRequested;

    private (RadioButton Nav, Control Page)[] Sections =>
        [(NavSound, SoundPage), (NavDecks, DecksPage), (NavScreen, ScreenPage), (NavLibrary, LibraryPage), (NavAbout, AboutPage)];

    /// <summary>The section showing.</summary>
    public SettingsSection Section
    {
        get => (SettingsSection)Math.Max(0, Array.FindIndex(Sections, s => s.Nav.IsChecked == true));
        set => Sections[(int)value].Nav.IsChecked = true;
    }

    /// <summary>Puts the keyboard on DONE, so keys don't reach the decks or the library behind.</summary>
    public void FocusDone() => DoneButton.Focus();

    private void ShowCheckedSection()
    {
        foreach ((RadioButton nav, Control page) in Sections)
            page.IsVisible = nav.IsChecked == true;
        if (AboutPage.IsVisible)
            FillAbout();
    }

    /// <summary>The About section reads the licence notices, so only when it's first shown.</summary>
    private void FillAbout()
    {
        if (_aboutFilled)
            return;
        _aboutFilled = true;
        VersionText.Text = About.Version;
        MakerText.Text = $"A simple, intuitive DJ app, made by {About.Maker}.";
        ProjectLink.Content = About.ProjectUrl.Replace("https://", "");
        BuiltWithText.Text = About.BuiltWith();
        NoticesButton.IsVisible = File.Exists(About.NoticesPath);
    }

    private void OnDoneClick(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void OnMixxxImportClick(object? sender, RoutedEventArgs e) => MixxxImportRequested?.Invoke(this, EventArgs.Empty);

    private async void OnProjectClick(object? sender, RoutedEventArgs e) =>
        await TopLevel.GetTopLevel(this)!.Launcher.LaunchUriAsync(new Uri(About.ProjectUrl));

    /// <summary>Opens the notices in the computer's own text viewer: they're long.</summary>
    private async void OnNoticesClick(object? sender, RoutedEventArgs e) =>
        await TopLevel.GetTopLevel(this)!.Launcher.LaunchFileInfoAsync(new FileInfo(About.NoticesPath));
}
