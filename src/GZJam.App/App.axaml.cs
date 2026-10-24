using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Avalonia;
using Avalonia.Controls;
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
            if (StartupWindow.IsShown)
            {
                var startup = new StartupWindow(settings.Window);
                startup.Show();
                _ = StartBehindAsync(desktop, settings, startup);
            }
            else
            {
                var viewModel = CreateViewModel(settings);
                CreateMainWindow(desktop, settings, viewModel);
                viewModel.Start();
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>How long the startup screen stays once everything is ready, for its sweep to reach the end.</summary>
    private static readonly TimeSpan StartupFinish = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Gets GZJam ready behind the startup screen a step at a time, letting it draw a frame between
    /// steps so its sweep follows along. The main window opens underneath it; once that's drawn and
    /// the outputs are open, the startup screen goes.
    /// </summary>
    private static async Task StartBehindAsync(IClassicDesktopStyleApplicationLifetime desktop, AppSettings settings, StartupWindow startup)
    {
        try
        {
            startup.BeginStep(0, 0.2);
            await NextFrame(startup);   // the startup screen is on screen
            Log.Info($"Startup screen up, {SinceStart:0.00} s after starting");
            var viewModel = CreateViewModel(settings);

            startup.BeginStep(0.2, 0.6);
            await NextFrame(startup);
            var window = CreateMainWindow(desktop, settings, viewModel);
            window.Show();

            startup.BeginStep(0.6, 0.85);
            await NextFrame(window);   // laid out and drawn, out of sight
            viewModel.Start();   // opens the outputs

            startup.Finish();
            await Task.Delay(StartupFinish);
            startup.Dismiss();
            Log.Info($"Ready, {SinceStart:0.00} s after starting");
        }
        catch (Exception ex)
        {
            // As before there was a startup screen: logged, and GZJam stops.
            startup.Dismiss();
            var failure = ExceptionDispatchInfo.Capture(ex);
            Dispatcher.UIThread.Post(failure.Throw);
        }
    }

    /// <summary>Seconds since GZJam was started, for the log.</summary>
    private static double SinceStart => (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalSeconds;

    /// <summary>Completes once <paramref name="window"/> has drawn its next frame.</summary>
    private static Task NextFrame(TopLevel window)
    {
        var drawn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.RequestAnimationFrame(_ => drawn.TrySetResult());
        return drawn.Task;
    }

    /// <summary>Opens the library and starts audio.</summary>
    private static MainWindowViewModel CreateViewModel(AppSettings settings)
    {
        var database = new LibraryDatabase(AppPaths.LibraryDatabase);
        var (audio, mix, audioError) = StartAudio();
        return new MainWindowViewModel(database, settings, audio, mix, audioError);
    }

    private static MainWindow CreateMainWindow(IClassicDesktopStyleApplicationLifetime desktop, AppSettings settings, MainWindowViewModel viewModel)
    {
        var window = new MainWindow { DataContext = viewModel };
        window.RestorePlacement(settings.Window);
        window.Opened += (_, _) =>
            Log.Info($"Window open, {SinceStart:0.00} s after starting");
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
        return window;
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
                Log.Info($"  output {device.Index}: {device} [{device.Driver}]" + (device.SystemName != device.Name ? $", called \"{device.SystemName}\" by the system" : ""));
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
