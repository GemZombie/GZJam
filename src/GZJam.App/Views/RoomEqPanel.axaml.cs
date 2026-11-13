using Avalonia.Controls;
using Avalonia.Interactivity;
using GZJam.App.ViewModels;

namespace GZJam.App.Views;

/// <summary>The room EQ panel (#272): the venues' names asked for and confirmed here.</summary>
public partial class RoomEqPanel : UserControl
{
    public RoomEqPanel() => InitializeComponent();

    private RoomEqViewModel? Model => DataContext as RoomEqViewModel;

    private Window? Owner => TopLevel.GetTopLevel(this) as Window;

    private async void OnNewVenueClick(object? sender, RoutedEventArgs e)
    {
        if (Model is not { } model || Owner is not { } owner)
            return;
        string? name = await new PromptDialog("New venue", "Its room EQ starts flat.", "Make it").ShowDialog<string?>(owner);
        if (name is not null)
            model.NewVenue(name);
    }

    private async void OnRenameVenueClick(object? sender, RoutedEventArgs e)
    {
        if (Model is not { CurrentVenue: { } venue } model || Owner is not { } owner)
            return;
        string? name = await new PromptDialog($"Rename \"{venue.Name}\"", "", "Rename", venue.Name).ShowDialog<string?>(owner);
        if (name is not null && name.Trim() != venue.Name)
            model.RenameVenue(name);
    }

    private async void OnDeleteVenueClick(object? sender, RoutedEventArgs e)
    {
        if (Model is not { CurrentVenue: { } venue } model || Owner is not { } owner)
            return;
        if (await new ConfirmDialog($"Delete the venue \"{venue.Name}\"?", "Its room EQ goes with it, and the speakers get the music flat.",
                "Delete").ShowDialog<bool>(owner))
            model.DeleteVenue();
    }
}
