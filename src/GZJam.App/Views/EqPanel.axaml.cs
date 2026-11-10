using Avalonia.Controls;
using GZJam.App.Controls;
using GZJam.App.ViewModels;

namespace GZJam.App.Views;

/// <summary>A deck's EQ, shown from its EQ button.</summary>
public partial class EqPanel : UserControl
{
    public EqPanel()
    {
        InitializeComponent();
        // A fader held is heard at every step, and saved once, when it's let go (#362).
        AddHandler(Fader.HeldEvent, (_, _) => (DataContext as EqEditor)?.BeginAdjusting());
        AddHandler(Fader.LetGoEvent, (_, _) => (DataContext as EqEditor)?.EndAdjusting());
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        // Closed, or shown for another song, while a fader was held: what it changed is saved.
        (_shown as EqEditor)?.EndAdjusting();
        _shown = DataContext;
        base.OnDataContextChanged(e);
    }

    private object? _shown;

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        (DataContext as EqEditor)?.EndAdjusting();
        base.OnDetachedFromVisualTree(e);
    }
}
