using Avalonia.Controls;
using GZJam.App.ViewModels;

namespace GZJam.App.Views;

/// <summary>
/// The Downloads window (#304): everything coming down from the servers. Opened from the top bar's
/// progress while it shows downloads, and from a server's right-click menu › Downloads…; one at a time.
/// </summary>
public partial class DownloadsWindow : Window
{
    public DownloadsWindow()
    {
        InitializeComponent();
        CloseButton.Click += (_, _) => Close();
    }

    public DownloadsWindow(DownloadsViewModel vm) : this()
    {
        DataContext = vm;
    }
}
