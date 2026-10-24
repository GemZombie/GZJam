using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace GZJam.App.Views;

/// <summary>
/// Menus on a button (BPM, Label, a deck's ⋯) whose choices change, so they're filled as they're
/// wanted. A menu shows the choices it held when it began to open: filled in its Opening event
/// it opened empty, a thin sliver of border under the button. So the choices are filled as the
/// button is pressed (finger, pen or mouse) or activated by the keyboard, before it opens.
/// </summary>
internal static class FlyoutMenus
{
    public static void FillBeforeOpening(Button button, MenuFlyout menu, Action fill)
    {
        bool filled = false;
        void FillNow()
        {
            fill();
            filled = true;
        }
        button.AddHandler(InputElement.PointerPressedEvent, (_, _) => FillNow(), RoutingStrategies.Tunnel, handledEventsToo: true);
        button.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space)
                FillNow();
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        menu.Opening += (_, _) =>
        {
            // Opened from code (ShowAt), nothing filled it: fill it now, for its items at least.
            // Filled already, it's left alone, so what shows is what's in it.
            if (!filled)
                fill();
            filled = false;
        };
    }
}
