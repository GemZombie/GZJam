/*
 * GZJam's own sound processing, in C (#344). BASS calls these on its audio thread, many times a
 * second. They were .NET code, and whenever .NET stopped GZJam to tidy its memory (a garbage
 * collection), the audio thread had to wait for it too: at Hot Jam on 2026-11-09 the room heard
 * gaps (#343). Code here runs no .NET, so nothing GZJam does can hold up the sound.
 *
 * The app owns the state each one works on (allocated by GZJam.Audio, laid out as in
 * NativeDsp.cs) and passes it as BASS's "user" pointer. The audio thread raises meter peaks and
 * flags; the window's thread takes them (exchanging them for 0) and sets the volume and mono.
 *
 * Built by native/build.sh (macOS, Linux) and native/build.cmd (Windows), and by the Native
 * workflow on GitHub; the libraries are kept in lib/bass/<platform> beside BASS's own.
 *
 * MIT License, as the rest of GZJam.
 */
#ifdef _MSC_VER
/* No headers and no C runtime on Windows: it builds with the compiler alone, needs nothing
   installed beside it, and has no runtime to start (linked with /NOENTRY /NODEFAULTLIB). */
typedef int int32_t;
typedef unsigned int uint32_t;
typedef unsigned long long uint64_t;
long _InterlockedCompareExchange(long volatile *destination, long exchange, long comparand);
#pragma intrinsic(_InterlockedCompareExchange)
int _fltused = 0;   /* floating point without the runtime */
#else
#include <stdint.h>
#endif

#ifdef _WIN32
#define EXPORT __declspec(dllexport)
#define CALLBACK __stdcall
#else
#define EXPORT __attribute__((visibility("default")))
#define CALLBACK
#endif

/* Bumped whenever a layout below changes, so the app can tell an old library from a new one. */
#define GZ_VERSION 1

typedef union { float f; int32_t i; } bits;

static int32_t compare_exchange(volatile int32_t *at, int32_t value, int32_t expected)
{
#ifdef _MSC_VER
    return _InterlockedCompareExchange((volatile long *)at, value, expected);
#else
    return __sync_val_compare_and_swap(at, expected, value);
#endif
}

/* Raises a peak the window's thread takes (and sets back to 0) now and then. */
static void raise_peak(volatile float *peak, float value)
{
    for (;;)
    {
        bits current, wanted;
        current.f = *peak;
        if (value <= current.f)
            return;
        wanted.f = value;
        if (compare_exchange((volatile int32_t *)peak, wanted.i, current.i) == current.i)
            return;
    }
}

static float magnitude(float x) { return x < 0 ? -x : x; }

/*
 * An output's last stage: its volume, mono fold-down, a safety limiter and its meter. Stereo
 * float samples.
 */
typedef struct
{
    volatile float volume;      /* 0 to 1; set by the app */
    volatile int32_t mono;      /* both sides the same; set by the app */
    float ceiling;              /* the limiter's ceiling, just under full scale */
    float noticeable;           /* gain below this lights the limiting light */
    float release;              /* how much of the gain reduction is left after each sample */
    float gain;                 /* the limiter's gain: the audio thread's alone */
    volatile float peak_left;   /* the loudest since the app last took them */
    volatile float peak_right;
    volatile int32_t limited;   /* the limiter turned things down noticeably since then */
} gz_bus;

EXPORT void CALLBACK gz_bus_dsp(uint32_t handle, uint32_t channel, void *buffer, uint32_t length, void *user)
{
    gz_bus *bus = (gz_bus *)user;
    float *samples = (float *)buffer;
    uint32_t count = length / sizeof(float);
    float volume = bus->volume, gain = bus->gain, release = bus->release, ceiling = bus->ceiling;
    float left = 0, right = 0;
    int mono = bus->mono != 0, limited = 0;
    (void)handle;
    (void)channel;
    for (uint32_t i = 0; i + 1 < count; i += 2)
    {
        float l = samples[i] * volume, r = samples[i + 1] * volume;
        if (mono)
            l = r = (l + r) / 2;

        /* Instant attack, smooth release: the gain drops at once to keep this frame under the
           ceiling, then recovers gradually so there's no pumping. */
        gain = 1 - (1 - gain) * release;
        float peak = magnitude(l) > magnitude(r) ? magnitude(l) : magnitude(r);
        if (peak * gain > ceiling)
        {
            gain = ceiling / peak;
            if (gain < bus->noticeable)
                limited = 1;
        }
        l *= gain;
        r *= gain;

        samples[i] = l;
        samples[i + 1] = r;
        if (magnitude(l) > left)
            left = magnitude(l);
        if (magnitude(r) > right)
            right = magnitude(r);
    }
    bus->gain = gain;
    if (limited)
        bus->limited = 1;
    raise_peak(&bus->peak_left, left);
    raise_peak(&bus->peak_right, right);
}

/* A deck's meter: the loudest sample, either side, since the app last took it. */
typedef struct
{
    volatile float peak;
} gz_meter;

EXPORT void CALLBACK gz_meter_dsp(uint32_t handle, uint32_t channel, void *buffer, uint32_t length, void *user)
{
    const float *samples = (const float *)buffer;
    uint32_t count = length / sizeof(float);
    float peak = 0;
    (void)handle;
    (void)channel;
    for (uint32_t i = 0; i < count; i++)
        if (magnitude(samples[i]) > peak)
            peak = magnitude(samples[i]);
    raise_peak(&((gz_meter *)user)->peak, peak);
}

/* BASS_ChannelSetPosition, passed in by the app: this library doesn't link to BASS. */
typedef int (CALLBACK *set_position)(uint32_t handle, uint64_t position, uint32_t mode);

/*
 * A deck's jump (the JUMP key): when the song reaches a point, it carries on from another.
 * Called as the audio is mixed, so the move is seamless; the app sees "jumped" and tidies up.
 */
typedef struct
{
    set_position set_position;
    uint64_t to;                /* bytes */
    volatile int32_t jumped;
} gz_jump;

EXPORT void CALLBACK gz_jump_sync(uint32_t handle, uint32_t channel, uint32_t data, void *user)
{
    gz_jump *jump = (gz_jump *)user;
    (void)handle;
    (void)data;
    if (jump->jumped)
        return;
    jump->set_position(channel, jump->to, 0 /* BASS_POS_BYTE */);
    jump->jumped = 1;
}

EXPORT int32_t gz_version(void)
{
    return GZ_VERSION;
}
