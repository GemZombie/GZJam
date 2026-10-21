namespace GZJam.Library;

/// <summary>
/// A tempo range the DJ has chosen for a track, when automatic BPM detection got it wrong.
/// Detection then picks the strongest tempo inside it. Ranges narrower than double (say
/// 140-220) leave exactly one choice between half and double time.
/// </summary>
public readonly record struct BpmRange(int Min, int Max)
{
    public bool Contains(double bpm) => bpm >= Min && bpm <= Max;

    public override string ToString() => $"{Min}-{Max}";
}
