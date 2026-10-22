using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
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
            Log.Start(AppPaths.DataDirectory);
            Log.Info($"GZJam starting on {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unhandled exception", e.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (_, e) => Log.Error("Unobserved task exception", e.Exception);
            Dispatcher.UIThread.UnhandledException += (_, e) => Log.Error("Unhandled UI exception", e.Exception);

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
            desktop.Exit += (_, _) =>
            {
                Log.Info("GZJam closing");
                viewModel.Dispose();
            };
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
            Log.Info($"BASS {audio.BassVersion}, BASS_FX {audio.FxVersion}, BASSmix {audio.MixVersion}, formats: {string.Join(", ", AudioEngine.LoadedAddOns)}");
            foreach (AudioDevice device in audio.GetOutputDevices())
                Log.Info($"  output {device.Index}: {device} [{device.Driver}]");
            return (audio, new MixEngine(audio), null);
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException or AudioException)
        {
            audio?.Dispose();
            Log.Error("Audio failed to start", ex);
            return (null, null, $"Audio isn't available: {ex.Message}");
        }
    }
}
