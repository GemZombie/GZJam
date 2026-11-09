using Avalonia.Controls;
using GZJam.App.ViewModels;

namespace GZJam.App.Views;

/// <summary>The headphone preview's player, wherever songs can be previewed from: its DataContext is the preview.</summary>
public partial class PreviewBar : UserControl
{
    public PreviewBar()
    {
        InitializeComponent();
        Seek.SeekRequested += (_, fraction) => (DataContext as PreviewViewModel)?.SeekTo(fraction);
    }
}
