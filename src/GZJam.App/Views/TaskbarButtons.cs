using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;
using GZJam.App.ViewModels;

namespace GZJam.App.Views;

/// <summary>
/// Windows: play/pause, fade out and auto-play under GZJam's taskbar button, in the window's
/// preview (a thumbnail toolbar, #262), so the room's song can be paused or faded without
/// switching to GZJam. Clicks arrive as WM_COMMAND through Avalonia's hook on the window; the
/// buttons follow <see cref="RoomControls"/>. Windows only lets them be added once its taskbar
/// button exists (it says so with "TaskbarButtonCreated", again if Explorer restarts).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class TaskbarButtons : IDisposable
{
    private const uint PlayPauseId = 1, FadeOutId = 2, AutoPlayId = 3;
    private const uint WM_COMMAND = 0x0111, THBN_CLICKED = 0x1800;

    private readonly Window _window;
    private readonly RoomControls _room;
    private readonly uint _buttonCreated = RegisterWindowMessage("TaskbarButtonCreated");
    private ITaskbarList3? _taskbar;
    private IntPtr _hwnd;
    private bool _added;
    private readonly Dictionary<RoomButtonIcons.Icon, IntPtr> _icons = [];

    private TaskbarButtons(Window window, RoomControls room)
    {
        _window = window;
        _room = room;
    }

    /// <summary>Puts the buttons under <paramref name="window"/>'s taskbar button, for as long as it's open.</summary>
    public static void Attach(Window window, RoomControls room)
    {
        var buttons = new TaskbarButtons(window, room);
        Win32Properties.AddWndProcHookCallback(window, buttons.WndProc);
        window.Opened += (_, _) => buttons.TryAdd();
        window.Closed += (_, _) => buttons.Dispose();
        room.Changed += buttons.Update;
    }

    private IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == _buttonCreated)
        {
            _hwnd = hwnd;
            _added = false;   // a new taskbar button (Explorer restarted): add them again
            TryAdd();
        }
        else if (message == WM_COMMAND && (uint)(wParam.ToInt64() >> 16 & 0xFFFF) == THBN_CLICKED)
        {
            switch ((uint)(wParam.ToInt64() & 0xFFFF))
            {
                case PlayPauseId: _room.PlayPause(); break;
                case FadeOutId: _room.FadeOut(); break;
                case AutoPlayId: _room.ToggleAutoPlay(); break;
                default: return IntPtr.Zero;
            }
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void TryAdd()
    {
        if (_added)
            return;
        try
        {
            if (_hwnd == IntPtr.Zero && _window.TryGetPlatformHandle() is { HandleDescriptor: "HWND" } handle)
                _hwnd = handle.Handle;
            if (_hwnd == IntPtr.Zero)
                return;
            if (_taskbar is null)
            {
                _taskbar = (ITaskbarList3)new TaskbarList();
                _taskbar.HrInit();
            }
            int result = _taskbar.ThumbBarAddButtons(_hwnd, 3, Buttons());
            if (result < 0)
                return;   // the taskbar button isn't there yet: "TaskbarButtonCreated" comes next
            _added = true;
            Log.Info("Taskbar buttons: play/pause, fade out, auto-play");
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or EntryPointNotFoundException)
        {
            Log.Error("Couldn't add the taskbar buttons", ex);
        }
    }

    private void Update()
    {
        if (!_added || _taskbar is null)
            return;
        try
        {
            _taskbar.ThumbBarUpdateButtons(_hwnd, 3, Buttons());
        }
        catch (COMException ex)
        {
            Log.Error("Couldn't update the taskbar buttons", ex);
        }
    }

    private THUMBBUTTON[] Buttons()
    {
        RoomControls.State state = _room.Current;
        return
        [
            Button(PlayPauseId, state.IsPlaying ? RoomButtonIcons.Icon.Pause : RoomButtonIcons.Icon.Play, state.PlayPauseTip, state.CanPlayPause),
            Button(FadeOutId, RoomButtonIcons.Icon.FadeOut, state.FadeOutTip, state.CanFadeOut),
            Button(AutoPlayId, state.IsAutoPlayOn ? RoomButtonIcons.Icon.AutoPlayOn : RoomButtonIcons.Icon.AutoPlayOff, state.AutoPlayTip,
                state.CanToggleAutoPlay),
        ];
    }

    private THUMBBUTTON Button(uint id, RoomButtonIcons.Icon icon, string tip, bool enabled) => new()
    {
        dwMask = THB_ICON | THB_TOOLTIP | THB_FLAGS,
        iId = id,
        hIcon = IconHandle(icon),
        szTip = tip.Length > 259 ? tip[..259] : tip,
        dwFlags = enabled ? THBF_ENABLED : THBF_DISABLED,
    };

    /// <summary>The icon at the size Windows uses for these buttons on this window's screen, made once.</summary>
    private IntPtr IconHandle(RoomButtonIcons.Icon icon)
    {
        if (_icons.TryGetValue(icon, out IntPtr made))
            return made;
        uint dpi = _hwnd != IntPtr.Zero ? GetDpiForWindow(_hwnd) : 96;
        int size = GetSystemMetricsForDpi(SM_CXSMICON, dpi == 0 ? 96 : dpi);
        byte[] png = RoomButtonIcons.Png(icon, size);
        IntPtr handle = CreateIconFromResourceEx(png, (uint)png.Length, true, 0x00030000, size, size, 0);
        _icons[icon] = handle;
        return handle;
    }

    public void Dispose()
    {
        _room.Changed -= Update;
        foreach (IntPtr icon in _icons.Values)
        {
            if (icon != IntPtr.Zero)
                DestroyIcon(icon);
        }
        _icons.Clear();
        if (_taskbar is not null)
            Marshal.ReleaseComObject(_taskbar);
        _taskbar = null;
    }

    // ───────────── Windows ─────────────

    private const uint THB_ICON = 0x2, THB_TOOLTIP = 0x4, THB_FLAGS = 0x8;
    private const uint THBF_ENABLED = 0x0, THBF_DISABLED = 0x1;
    private const int SM_CXSMICON = 49;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct THUMBBUTTON
    {
        public uint dwMask;
        public uint iId;
        public uint iBitmap;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szTip;
        public uint dwFlags;
    }

    [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090"), ClassInterface(ClassInterfaceType.None)]
    private class TaskbarList;

    /// <summary>ITaskbarList3, with ITaskbarList's and ITaskbarList2's methods first, in their order.</summary>
    [ComImport, Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        [PreserveSig] int HrInit();
        [PreserveSig] int AddTab(IntPtr hwnd);
        [PreserveSig] int DeleteTab(IntPtr hwnd);
        [PreserveSig] int ActivateTab(IntPtr hwnd);
        [PreserveSig] int SetActiveAlt(IntPtr hwnd);
        [PreserveSig] int MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        [PreserveSig] int SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
        [PreserveSig] int SetProgressState(IntPtr hwnd, int flags);
        [PreserveSig] int RegisterTab(IntPtr tab, IntPtr hwnd);
        [PreserveSig] int UnregisterTab(IntPtr tab);
        [PreserveSig] int SetTabOrder(IntPtr tab, IntPtr insertBefore);
        [PreserveSig] int SetTabActive(IntPtr tab, IntPtr hwnd, uint reserved);
        [PreserveSig] int ThumbBarAddButtons(IntPtr hwnd, uint count, [MarshalAs(UnmanagedType.LPArray)] THUMBBUTTON[] buttons);
        [PreserveSig] int ThumbBarUpdateButtons(IntPtr hwnd, uint count, [MarshalAs(UnmanagedType.LPArray)] THUMBBUTTON[] buttons);
        [PreserveSig] int ThumbBarSetImageList(IntPtr hwnd, IntPtr imageList);
        [PreserveSig] int SetOverlayIcon(IntPtr hwnd, IntPtr icon, [MarshalAs(UnmanagedType.LPWStr)] string? description);
        [PreserveSig] int SetThumbnailTooltip(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] string? tip);
        [PreserveSig] int SetThumbnailClip(IntPtr hwnd, IntPtr clip);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconFromResourceEx(byte[] data, uint size, [MarshalAs(UnmanagedType.Bool)] bool icon,
        uint version, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
