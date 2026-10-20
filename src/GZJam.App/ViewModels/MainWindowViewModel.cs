using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using GZJam.Library;

namespace GZJam.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _clock;

    public MainWindowViewModel(LibraryDatabase database, AppSettings settings)
    {
        _settings = settings;
        Library = new LibraryViewModel(database);
        Queue = new QueueViewModel();
        _clock = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => UpdateClock());
        UpdateClock();
    }

    public LibraryViewModel Library { get; }
    public QueueViewModel Queue { get; }

    [ObservableProperty]
    public partial string Clock { get; private set; } = "";

    private void UpdateClock() => Clock = DateTime.Now.ToString("t");

    public void Start()
    {
        _clock.Start();
        _ = Library.StartAsync(_settings.SelectedRootPath, _settings.SelectedFolder);
    }

    /// <summary>Copies state worth remembering into the settings before they're saved.</summary>
    public void SaveState()
    {
        (_settings.SelectedRootPath, _settings.SelectedFolder) = Library.GetSelection();
    }

    public void Dispose()
    {
        _clock.Stop();
        Library.Dispose();
    }
}
