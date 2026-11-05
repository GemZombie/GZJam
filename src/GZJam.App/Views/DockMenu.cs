using Avalonia;
using Avalonia.Controls;
using GZJam.App.ViewModels;

namespace GZJam.App.Views;

/// <summary>
/// Mac: play/pause, fade out and auto-play in the menu of GZJam's Dock icon (right-click or
/// Control-click it, #262), following <see cref="RoomControls"/>. A first click on air that needs
/// a second (Settings' two taps) arms it; the menu then says to click again.
/// </summary>
internal sealed class DockMenu
{
    private readonly RoomControls _room;
    private readonly NativeMenuItem _playPause = new(), _fadeOut = new(), _autoPlay = new() { ToggleType = MenuItemToggleType.CheckBox };

    private DockMenu(RoomControls room)
    {
        _room = room;
        _playPause.Click += (_, _) => _room.PlayPause();
        _fadeOut.Click += (_, _) => _room.FadeOut();
        _autoPlay.Click += (_, _) => _room.ToggleAutoPlay();
    }

    /// <summary>Gives the Dock icon the menu, for as long as <paramref name="window"/> is open.</summary>
    public static void Attach(Window window, RoomControls room)
    {
        if (Application.Current is not { } app)
            return;
        var menu = new DockMenu(room);
        menu.Update();
        NativeDock.SetMenu(app, new NativeMenu { Items = { menu._playPause, menu._fadeOut, menu._autoPlay } });
        room.Changed += menu.Update;
        window.Closed += (_, _) => room.Changed -= menu.Update;
        Log.Info("Dock menu: play/pause, fade out, auto-play");
    }

    private void Update()
    {
        RoomControls.State state = _room.Current;
        _playPause.Header = state.PlayPauseTip;
        _playPause.IsEnabled = state.CanPlayPause;
        _fadeOut.Header = state.FadeOutTip;
        _fadeOut.IsEnabled = state.CanFadeOut;
        _autoPlay.Header = state.IsAutoPlayOn ? "Auto-play" : state.AutoPlayTip;
        _autoPlay.IsChecked = state.IsAutoPlayOn;
        _autoPlay.IsEnabled = state.CanToggleAutoPlay;
    }
}
