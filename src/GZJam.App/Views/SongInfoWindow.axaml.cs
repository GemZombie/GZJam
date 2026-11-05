using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GZJam.App.ViewModels;

namespace GZJam.App.Views;

/// <summary>
/// A song's info, or several songs' (#129). Not modal, so the decks are a click away while it's
/// open, during a night too. One at a time: another song's info takes its place. Escape closes
/// it; with changes not saved, it asks first.
/// </summary>
public partial class SongInfoWindow : Window
{
    private bool _discarding;

    public SongInfoWindow()
    {
        InitializeComponent();
        RevealButton.Content = FileManager.RevealLabel.ToUpperInvariant();
    }

    public SongInfoWindow(SongInfoViewModel vm) : this()
    {
        DataContext = vm;
        RevealButton.IsEnabled = vm.Problem is null;
        // Enter saves; with nothing to save (or nothing that can be), it closes, as before.
        SaveButton.IsDefault = vm.IsEditable;
        CloseButton.IsDefault = !vm.IsEditable;
        vm.ConfirmAsync = (heading, message, confirm) =>
        {
            var dialog = new ConfirmDialog(heading, message, confirm);
            dialog.ConfirmIsHarmless();
            return dialog.ShowDialog<bool>(this);
        };
        ConfirmDiscardAsync = () => new ConfirmDialog($"Discard your changes to {(vm.IsSingle ? $"\"{vm.Heading}\"" : vm.Heading)}?",
            "They haven't been saved.", "Discard", cancelText: "Keep editing", cancelIsDefault: true).ShowDialog<bool>(this);
        Opened += async (_, _) => await vm.LoadAsync();
    }

    internal SongInfoViewModel Vm => (SongInfoViewModel)DataContext!;

    /// <summary>Asks whether changes not saved can be thrown away; true when they can (or there are none).</summary>
    internal Func<Task<bool>> ConfirmDiscardAsync { get; set; } = () => Task.FromResult(true);

    /// <summary>Closes it, changes not saved and all: another song's info is opened in its place by GZJam itself.</summary>
    internal void CloseWithoutAsking()
    {
        _discarding = true;
        Close();
    }

    /// <summary>Whether the window can go: no changes, or the DJ said to discard them. Then it closes without asking again.</summary>
    internal async Task<bool> AskToDiscardAsync()
    {
        if (_discarding || !Vm.HasChanges)
            return true;
        _discarding = await ConfirmDiscardAsync();
        return _discarding;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_discarding || !Vm.HasChanges || e.CloseReason is WindowCloseReason.OwnerWindowClosing or WindowCloseReason.ApplicationShutdown
                or WindowCloseReason.OSShutdown)
            return;
        e.Cancel = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            if (await AskToDiscardAsync())
                Close();
        });
    }

    private void OnRevealClick(object? sender, RoutedEventArgs e) => FileManager.Reveal(Vm.Location);

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnSaveClick(object? sender, RoutedEventArgs e) => await Vm.SaveAsync();

    private async void OnUndoClick(object? sender, RoutedEventArgs e) => await Vm.UndoAsync();

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Vm.Cancel();

    private async void OnUseFileTagsClick(object? sender, RoutedEventArgs e) => await Vm.UseFileTagsAsync();

    private void OnAddTagClick(object? sender, RoutedEventArgs e)
    {
        Vm.AddTag();
        NewTagNameBox.Focus();
    }

    private void OnAddLabelClick(object? sender, RoutedEventArgs e)
    {
        Vm.AddLabel();
        NewLabelBox.Focus();
    }

    /// <summary>Enter in the new tag's boxes adds it, rather than saving.</summary>
    private void OnNewTagKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Vm.NewTagName.Trim().Length == 0)
            return;
        e.Handled = true;
        OnAddTagClick(sender, e);
    }

    private void OnNewLabelKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Vm.NewLabel.Trim().Length == 0)
            return;
        e.Handled = true;
        OnAddLabelClick(sender, e);
    }
}
