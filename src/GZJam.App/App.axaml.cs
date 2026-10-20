using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GZJam.App.ViewModels;
using GZJam.App.Views;
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
            var viewModel = new MainWindowViewModel(database, settings);

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
}
