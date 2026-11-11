using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using GZJam.App.ViewModels;

namespace GZJam.App.Views;

/// <summary>The headphone preview's player, wherever songs can be previewed from: its DataContext is the preview.</summary>
public partial class PreviewBar : UserControl
{
    /// <summary>
    /// The song can be acted on from the bar (#386): QUEUE, ⋯, a drag and a right-click. Under the
    /// song list only; Organize music has no queue in view.
    /// </summary>
    public static readonly StyledProperty<bool> ShowsSongActionsProperty =
        AvaloniaProperty.Register<PreviewBar, bool>(nameof(ShowsSongActions));

    public bool ShowsSongActions
    {
        get => GetValue(ShowsSongActionsProperty);
        set => SetValue(ShowsSongActionsProperty, value);
    }

    /// <summary>A hand over the song when it can be dragged, as over a row's grip.</summary>
    public static readonly DirectProperty<PreviewBar, Cursor?> GripCursorProperty =
        AvaloniaProperty.RegisterDirect<PreviewBar, Cursor?>(nameof(GripCursor), b => b.GripCursor);

    private static Cursor? _hand;

    public Cursor? GripCursor => ShowsSongActions ? _hand ??= new Cursor(StandardCursorType.Hand) : null;

    public PreviewBar()
    {
        InitializeComponent();
        Seek.SeekRequested += (_, fraction) => (DataContext as PreviewViewModel)?.SeekTo(fraction);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ShowsSongActionsProperty)
            RaisePropertyChanged(GripCursorProperty, null, GripCursor);
    }
}
