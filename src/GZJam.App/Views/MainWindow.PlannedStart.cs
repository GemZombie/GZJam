using Avalonia.Interactivity;
using GZJam.App.ViewModels;

namespace GZJam.App.Views;

/// <summary>
/// The planned start in the queue's header: a button with a date and time to pick, which the
/// queue's "Plays at" times and the time it ends are counted from until the music starts
/// (<see cref="PlannedStartViewModel"/>).
/// </summary>
public partial class MainWindow
{
    private void SetUpPlannedStart()
    {
        // The flyout isn't in the window's tree until it opens, so it is given its view model here.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
                QueueStartPanel.DataContext = vm.Plan;
        };
    }

    private void OnQueueStartFlyoutOpened(object? sender, EventArgs e) => Vm.Plan.PrepareDraft();

    private void OnQueueStartSetClick(object? sender, RoutedEventArgs e)
    {
        Vm.Plan.SetStartCommand.Execute(null);
        QueueStartButton.Flyout?.Hide();
    }

    private void OnQueueStartClearClick(object? sender, RoutedEventArgs e)
    {
        Vm.Plan.ClearStartCommand.Execute(null);
        QueueStartButton.Flyout?.Hide();
    }
}
