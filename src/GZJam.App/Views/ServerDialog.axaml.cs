using Avalonia.Controls;
using GZJam.App.ViewModels;
using GZJam.Library.Servers;

namespace GZJam.App.Views;

/// <summary>
/// Adds a music server, or changes one: address, user name, password and a name. Signs in before
/// saving, and says plainly what's wrong if it can't; a server that doesn't answer (away from its
/// network) can be saved anyway. Closes with true once saved.
/// </summary>
public partial class ServerDialog : Window
{
    private readonly ServersViewModel? _servers;
    private readonly ServerItemViewModel? _existing;
    private readonly CancellationTokenSource _cancel = new();
    private string _okText = "ADD";
    private bool _saveAnyway;
    private bool _busy;

    public ServerDialog() : this(null, null)
    {
    }

    public ServerDialog(ServersViewModel? servers, ServerItemViewModel? existing)
    {
        InitializeComponent();
        _servers = servers;
        _existing = existing;
        if (existing is { Server: var server })
        {
            Heading.Text = $"CHANGE {server.Name.ToUpperInvariant()}";
            _okText = "SAVE";
            AddressBox.Text = server.Address;
            UserBox.Text = server.UserName;
            NameBox.Text = server.Name;
            PasswordBox.PlaceholderText = "Unchanged";
        }
        OkButton.Content = _okText;

        // Text changes as it's set, not later (TextChanged comes after layout): a problem shown is
        // for what was typed when ADD was pressed.
        foreach (TextBox box in (TextBox[])[AddressBox, UserBox, PasswordBox, NameBox])
            box.PropertyChanged += (_, e) =>
            {
                if (e.Property != TextBox.TextProperty)
                    return;
                Edited();
                if (box == AddressBox)
                    NameBox.PlaceholderText = SubsonicClient.NormalizeAddress(AddressBox.Text) is { } address
                        ? ServersViewModel.DefaultName(address)
                        : "";
            };
        OkButton.Click += async (_, _) => await SaveAsync();
        CancelButton.Click += (_, _) => Close(false);
        Closed += (_, _) => _cancel.Cancel();
        Opened += (_, _) => (existing is null ? AddressBox : PasswordBox).Focus();
    }

    /// <summary>A field changed: the last problem no longer applies.</summary>
    private void Edited()
    {
        if (_busy)
            return;
        _saveAnyway = false;
        OkButton.Content = _okText;
    }

    private async Task SaveAsync()
    {
        if (_servers is null || _busy)
            return;
        _busy = true;
        SetEnabled(false);
        Say("Signing in…", problem: false);
        try
        {
            var edit = new ServerEdit(NameBox.Text ?? "", AddressBox.Text ?? "", UserBox.Text ?? "", PasswordBox.Text);
            ServerSaveResult result = await _servers.SaveAsync(_existing, edit, _saveAnyway, _cancel.Token);
            if (result.Saved)
            {
                Close(true);
                return;
            }
            Say(result.Problem ?? "", problem: true);
            _saveAnyway = result.CanSaveAnyway;
            OkButton.Content = _saveAnyway ? _okText + " ANYWAY" : _okText;
        }
        catch (OperationCanceledException)
        {
            // closed while signing in
        }
        finally
        {
            _busy = false;
            SetEnabled(true);
        }
    }

    private void SetEnabled(bool enabled)
    {
        foreach (Control control in (Control[])[AddressBox, UserBox, PasswordBox, NameBox, OkButton])
            control.IsEnabled = enabled;
    }

    private void Say(string text, bool problem)
    {
        Message.Text = text;
        Message.IsVisible = text.Length > 0;
        Message.Classes.Set("problem", problem);
    }
}
