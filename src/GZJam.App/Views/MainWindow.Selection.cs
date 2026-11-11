using Avalonia.Controls;

namespace GZJam.App.Views;

/// <summary>
/// One clear selection (#377): only the list the keys act on shows its selection in full (the
/// "active" class, styled in AppStyles.axaml); the others keep theirs, faintly. The keys act on
/// the queue or History while the keyboard is in them, on the folders, playlists or sources while
/// it's there, and otherwise on the song list (Ctrl+Alt+1, Ctrl+I, Enter), so that's the rule.
/// </summary>
public partial class MainWindow
{
    /// <summary>The lists that can be the one the keys act on, the song list last: it's the one otherwise.</summary>
    private Control[] SelectionLists => [QueueList, HistoryList, FolderTree, PlaylistList, SourceList, TrackGrid];

    /// <summary>The list the keys act on now, and shown so.</summary>
    internal Control ActiveList => SelectionLists.FirstOrDefault(list => list.IsKeyboardFocusWithin) ?? TrackGrid;

    private void InitializeSelection()
    {
        foreach (Control list in SelectionLists)
        {
            list.PropertyChanged += (_, e) =>
            {
                if (e.Property == IsKeyboardFocusWithinProperty)
                    ShowActiveList();
            };
        }
        ShowActiveList();
    }

    private void ShowActiveList()
    {
        Control active = ActiveList;
        foreach (Control list in SelectionLists)
            list.Classes.Set("active", list == active);
    }
}
