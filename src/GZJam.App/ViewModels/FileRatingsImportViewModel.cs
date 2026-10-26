using CommunityToolkit.Mvvm.ComponentModel;
using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>
/// Settings › Ratings in the song files: reads the star ratings other players keep in the files
/// of GZJam's unrated songs (see <see cref="FileRatings"/>), shows what they'd become, and gives
/// the hearts once the DJ says so.
/// </summary>
public sealed partial class FileRatingsImportViewModel(LibraryDatabase database, Action imported) : ObservableObject
{
    private FileRatings? _plan;
    private CancellationTokenSource? _reading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    public partial bool IsBusy { get; private set; }

    /// <summary>What's going on, or what was found.</summary>
    [ObservableProperty]
    public partial string Status { get; private set; } = "Reading the song files…";

    /// <summary>How 4-5, 3 and 1-2 stars come over; shown once something was found.</summary>
    [ObservableProperty]
    public partial string Mapping { get; private set; } = "";

    public bool CanImport => !IsBusy && _plan is { Count: > 0 };

    /// <summary>Reads the files. Canceled by <see cref="Cancel"/>, e.g. when the dialog closes.</summary>
    public async Task LoadAsync()
    {
        _reading = new CancellationTokenSource();
        CancellationToken cancel = _reading.Token;
        IsBusy = true;
        var progress = new Progress<(int Done, int Total)>(p =>
        {
            if (IsBusy)
                Status = $"Reading the song files… {Format.Count(p.Done)} of {Format.Count(p.Total)}";
        });
        try
        {
            _plan = await Task.Run(() => FileRatings.Plan(database, progress, cancel), cancel);
            IsBusy = false;
            Show(_plan);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Cancel() => _reading?.Cancel();

    private void Show(FileRatings plan)
    {
        string read = $"{Format.Count(plan.SongsRead)} unrated {(plan.SongsRead == 1 ? "song" : "songs")} read";
        if (plan.Count == 0)
        {
            Status = plan.ThreeStars > 0
                ? $"{read}: {Songs(plan.ThreeStars)} with 3 stars, which stay unrated. Nothing to import."
                : $"{read}: none has a star rating in its file. Nothing to import.";
            return;
        }
        var found = new List<string>();
        if (plan.Loved > 0)
            found.Add($"{Songs(plan.Loved)} with 4 or 5 stars {Get(plan.Loved)} ♥");
        if (plan.Rejected > 0)
            found.Add($"{Songs(plan.Rejected)} with 1 or 2 stars {Get(plan.Rejected)} a broken heart");
        if (plan.ThreeStars > 0)
            found.Add($"{Songs(plan.ThreeStars)} with 3 stars {(plan.ThreeStars == 1 ? "stays" : "stay")} unrated");
        Status = $"{read}. {Capitalized(string.Join("; ", found))}.";
        Mapping = "Songs you've rated in GZJam keep their rating.";
    }

    /// <summary>Gives the hearts. Returns how many songs got one, or null if nothing could.</summary>
    public async Task<int?> ImportAsync()
    {
        if (!CanImport || _plan is not { } plan)
            return null;
        IsBusy = true;
        try
        {
            int rated = await Task.Run(() => plan.Apply(database));
            Log.Info($"Imported ratings from the song files: {plan.Loved} ♥, {plan.Rejected} broken hearts ({rated} given)");
            imported();
            return rated;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>The notice after an import.</summary>
    public static string Summary(int rated) =>
        rated == 0 ? "No ratings imported: those songs were rated in GZJam meanwhile."
        : $"Ratings imported from the song files for {Songs(rated)}.";

    private static string Songs(int n) => $"{Format.Count(n)} {(n == 1 ? "song" : "songs")}";

    private static string Get(int n) => n == 1 ? "gets" : "get";

    private static string Capitalized(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
