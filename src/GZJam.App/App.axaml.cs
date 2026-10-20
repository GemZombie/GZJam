using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GZJam.App.ViewModels;
using GZJam.App.Views;
using GZJam.Audio;
using GZJam.Library;

namespace GZJam.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = AppSettings.Load(AppPaths.SettingsFile);
            var database = new LibraryDatabase(AppPaths.LibraryDatabase);
            var (audio, mix, audioError) = StartAudio();
            var viewModel = new MainWindowViewModel(database, settings, audio, mix, audioError);

            var window = new MainWindow { DataContext = viewModel };
            window.RestorePlacement(settings.Window);
            window.Closing += (_, _) =>
            {
                settings.Window = window.CapturePlacement();
                viewModel.SaveState();
                settings.Save(AppPaths.SettingsFile);
            };
            desktop.Exit += (_, _) => viewModel.Dispose();
            desktop.MainWindow = window;

            viewModel.Start();
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Loads BASS. If that fails the library still works; the window explains why there's no sound.</summary>
    private static (AudioEngine?, MixEngine?, string?) StartAudio()
    {
        AudioEngine? audio = null;
        try
        {
            audio = new AudioEngine();
            return (audio, new MixEngine(audio), null);
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException or AudioException)
        {
            audio?.Dispose();
            return (null, null, $"Audio isn't available: {ex.Message}");
        }
    }
}
