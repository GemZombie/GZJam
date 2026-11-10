using System.Reflection;
using System.Runtime.InteropServices;
using ManagedBass;

namespace GZJam.Audio;

/// <summary>
/// GZJam's own sound processing in native code (native/gzjam_dsp.c, #344): the output stage
/// (volume, mono, limiter, meter), the decks' meters and their jumps. BASS calls these on its
/// audio thread, many times a second. They used to be .NET code, and when .NET stopped GZJam to
/// tidy its memory (a garbage collection) the audio thread waited too: at Hot Jam on 2026-11-09
/// the room heard gaps (#343). Native code can't be stopped by .NET, so nothing GZJam does can
/// hold up the sound.
/// The state each one works on is native memory GZJam owns, laid out as in the C file; the
/// audio thread raises peaks and flags in it, and the window's thread takes them.
/// </summary>
internal static unsafe class NativeDsp
{
    private const string Library = "gzjam_dsp";

    /// <summary>The library's GZ_VERSION: the layouts below match it.</summary>
    private const int Version = 1;

    private static readonly Lazy<Exports> Loaded = new(Load);

    /// <summary>
    /// Fails, with what to do, when the library is missing or old: GZJam won't play without it.
    /// </summary>
    private static Exports Functions => Loaded.Value;

    private sealed record Exports(IntPtr BusDsp, IntPtr MeterDsp, IntPtr JumpSync, IntPtr SetPosition);

    private static Exports Load()
    {
        Assembly assembly = typeof(NativeDsp).Assembly;
        if (!NativeLibrary.TryLoad(Library, assembly, null, out IntPtr library))
            throw new AudioException($"GZJam's sound processing ({Library}) is missing: reinstall GZJam", Errors.FileOpen);
        var version = (delegate* unmanaged<int>)NativeLibrary.GetExport(library, "gz_version");
        if (version() != Version)
            throw new AudioException($"GZJam's sound processing ({Library}) is from another version: reinstall GZJam", Errors.Version);
        IntPtr bass = NativeLibrary.Load("bass", typeof(Bass).Assembly, null);
        return new Exports(
            NativeLibrary.GetExport(library, "gz_bus_dsp"),
            NativeLibrary.GetExport(library, "gz_meter_dsp"),
            NativeLibrary.GetExport(library, "gz_jump_sync"),
            NativeLibrary.GetExport(bass, "BASS_ChannelSetPosition"));
    }

    // BASS's own calls, taking a native callback where ManagedBass takes a .NET one.
    [DllImport("bass", EntryPoint = "BASS_ChannelSetDSP")]
    private static extern int ChannelSetDSP(int handle, IntPtr proc, IntPtr user, int priority);

    [DllImport("bass", EntryPoint = "BASS_ChannelSetSync")]
    private static extern int ChannelSetSync(int handle, SyncFlags type, long parameter, IntPtr proc, IntPtr user);

    private static T* Allocate<T>() where T : unmanaged => (T*)NativeMemory.AllocZeroed((nuint)sizeof(T));

    /// <summary>
    /// Frees state from <see cref="Allocate{T}"/>. Only once BASS can't call with it any more:
    /// after the stream it's on is freed, or the DSP or sync removed.
    /// </summary>
    internal static void Free(void* state) => NativeMemory.Free(state);

    // ── An output's last stage ──────────────────────────────────────────────────────────────

    /// <summary>As gz_bus in the C file.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Bus
    {
        public float Volume;
        public int Mono;
        public float Ceiling;
        public float Noticeable;
        public float Release;
        public float Gain;
        public float PeakLeft;
        public float PeakRight;
        public int Limited;
    }

    internal static Bus* NewBus(float ceiling, float noticeable, float release)
    {
        _ = Functions;   // missing or old: fails before anything's allocated
        Bus* bus = Allocate<Bus>();
        bus->Volume = 1;
        bus->Ceiling = ceiling;
        bus->Noticeable = noticeable;
        bus->Release = release;
        bus->Gain = 1;
        return bus;
    }

    internal static void SetBusDsp(int handle, Bus* bus)
    {
        if (ChannelSetDSP(handle, Functions.BusDsp, (IntPtr)bus, 0) == 0)
            throw new AudioException("Could not set up an output's volume and limiter", Bass.LastError);
    }

    // ── A deck's meter ───────────────────────────────────────────────────────────────────────

    /// <summary>As gz_meter in the C file.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Meter
    {
        public float Peak;
    }

    internal static Meter* NewMeter()
    {
        _ = Functions;
        return Allocate<Meter>();
    }

    internal static void SetMeterDsp(int handle, Meter* meter)
    {
        if (ChannelSetDSP(handle, Functions.MeterDsp, (IntPtr)meter, 0) == 0)
            throw new AudioException("Could not set up a deck's meter", Bass.LastError);
    }

    // ── A deck's jump ────────────────────────────────────────────────────────────────────────

    /// <summary>As gz_jump in the C file.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Jump
    {
        public IntPtr SetPosition;
        public ulong To;
        public int Jumped;
    }

    internal static Jump* NewJump()
    {
        Jump* jump = Allocate<Jump>();
        jump->SetPosition = Functions.SetPosition;
        return jump;
    }

    /// <summary>
    /// When <paramref name="handle"/> reaches <paramref name="at"/> bytes, it carries on from
    /// <paramref name="jump"/>'s To, as the audio is mixed (seamless), once. 0 if BASS refused.
    /// </summary>
    internal static int SetJumpSync(int handle, long at, Jump* jump) =>
        ChannelSetSync(handle, SyncFlags.Position | SyncFlags.Mixtime | SyncFlags.Onetime, at, Functions.JumpSync, (IntPtr)jump);
}
