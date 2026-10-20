using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace GZJam.App.Views;

public partial class DeckView : UserControl
{
    public static readonly StyledProperty<string> LetterProperty =
        AvaloniaProperty.Register<DeckView, string>(nameof(Letter), "A");

    public static readonly StyledProperty<IBrush?> AccentProperty =
        AvaloniaProperty.Register<DeckView, IBrush?>(nameof(Accent));

    public DeckView() => InitializeComponent();

    public string Letter
    {
        get => GetValue(LetterProperty);
        set => SetValue(LetterProperty, value);
    }

    public IBrush? Accent
    {
        get => GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }
}
