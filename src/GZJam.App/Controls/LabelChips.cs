using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;

namespace GZJam.App.Controls;

/// <summary>
/// A song's labels as small gray chips in a row (style <c>Border.label-chip</c>), for the library,
/// the queue and the decks. Only shows them: labels are set from the Labels menu.
/// </summary>
public sealed class LabelChips : ItemsControl
{
    private static readonly FuncTemplate<Panel?> Row = new(() => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 });

    public LabelChips()
    {
        ItemsPanel = Row;
        ItemTemplate = new FuncDataTemplate<string>((_, _) =>
            new Border { Classes = { "label-chip" }, Child = new TextBlock { [!TextBlock.TextProperty] = new Binding() } },
            supportsRecycling: true);
        IsHitTestVisible = false;
        VerticalAlignment = VerticalAlignment.Center;
    }

    protected override Type StyleKeyOverride => typeof(ItemsControl);
}
