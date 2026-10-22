using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using GZJam.App.ViewModels;
using GZJam.Library;

namespace GZJam.App.Views;

/// <summary>Settings › Import from Mixxx. Closes with what was imported, or null if cancelled.</summary>
public partial class MixxxImportDialog : Window
{
    public MixxxImportDialog()
    {
        InitializeComponent();
    }

    public MixxxImportDialog(MixxxImportViewModel vm) : this()
    {
        DataContext = vm;
        Opened += async (_, _) =>
        {
            if (vm.Source is null)
                await vm.LoadAsync();   // where Mixxx keeps its library on this computer
        };
    }

    private MixxxImportViewModel Vm => (MixxxImportViewModel)DataContext!;

    private async void OnChooseClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Mixxx's library (mixxxdb.sqlite)",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Mixxx library") { Patterns = ["*.sqlite"] }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            await Vm.LoadAsync(path);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    private async void OnImportClick(object? sender, RoutedEventArgs e)
    {
        if (await Vm.ImportAsync() is MixxxImportResult result)
            Close(result);
    }
}
