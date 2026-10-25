using Avalonia;
using Avalonia.Markup.Xaml;

namespace GZJam.App.Styles;

/// <summary>Touch mode's finger-sized controls (see TouchStyles.axaml), added to the app's styles while it's on.</summary>
public partial class TouchStyles : Avalonia.Styling.Styles
{
    private static TouchStyles? _instance;
    private static readonly Dictionary<object, object?> _usualSizes = [];

    public TouchStyles() => AvaloniaXamlLoader.Load(this);

    /// <summary>Turns touch mode's sizes on or off for every window, menu and flyout.</summary>
    public static void Apply(bool on)
    {
        if (Application.Current is not { } app)
            return;
        _instance ??= new TouchStyles();
        bool applied = app.Styles.Contains(_instance);
        if (on && !applied)
        {
            app.Styles.Add(_instance);
            // The app's own resources are found before a style's, so the sizes in App.axaml are
            // swapped for touch mode's and put back afterward.
            foreach (var (key, value) in _instance.Resources)
            {
                _usualSizes[key] = app.Resources.TryGetValue(key, out object? usual) ? usual : null;
                app.Resources[key] = value;
            }
        }
        else if (!on && applied)
        {
            app.Styles.Remove(_instance);
            foreach (var (key, usual) in _usualSizes)
            {
                if (usual is null)
                    app.Resources.Remove(key);
                else
                    app.Resources[key] = usual;
            }
            _usualSizes.Clear();
        }
    }
}
