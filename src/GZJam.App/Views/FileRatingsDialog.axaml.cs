using Avalonia.Controls;
using Avalonia.Interactivity;
using GZJam.App.ViewModels;

namespace GZJam.App.Views;

/// <summary>Settings › Ratings in the song files. Closes with how many songs got a rating, or null if canceled.</summary>
public partial class FileRatingsDialog : Window
{
    public FileRatingsDialog()
    {
        InitializeComponent();
    }

    public FileRatingsDialog(FileRatingsImportViewModel vm) : this()
    {
        DataContext = vm;
        Opened += async (_, _) => await vm.LoadAsync();
        Closed += (_, _) => vm.Cancel();   // stop reading files nobody's waiting for
    }

    private FileRatingsImportViewModel Vm => (FileRatingsImportViewModel)DataContext!;

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    private async void OnImportClick(object? sender, RoutedEventArgs e)
    {
        if (await Vm.ImportAsync() is int rated)
            Close(rated);
    }
}
