using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GZJam.App.ViewModels;
using GZJam.App.Views;
using GZJam.Library.Servers;

namespace GZJam.App.Tests;

/// <summary>Settings › Servers (#282): adding a music server, signing in, and what each server's card says.</summary>
public sealed class ServersAppTests
{
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>A pretend Navidrome: "gzjam" / "sesame" signs in; anything at "nowhere" doesn't answer.</summary>
    private static void PretendServer(AppHarness app, bool canDownload = true) =>
        app.ViewModel.Servers.CheckServer = (address, user, password, _) => Task.FromResult(
            address.Contains("nowhere", StringComparison.Ordinal) ? new ServerCheck(ServerCheckOutcome.Unreachable)
            : user == "gzjam" && password == "sesame"
                ? new ServerCheck(ServerCheckOutcome.Connected, "navidrome", "0.64.2", true, canDownload, 17786)
                : new ServerCheck(ServerCheckOutcome.WrongSignIn, "navidrome", "0.64.2", true));

    private static ServerDialog OpenDialog(AppHarness app, ServerItemViewModel? existing = null)
    {
        var dialog = new ServerDialog(app.ViewModel.Servers, existing);
        dialog.Show(app.Window);
        AppHarness.Settle(50);
        return dialog;
    }

    private static void Fill(ServerDialog dialog, string? address = null, string? user = null, string? password = null, string? name = null)
    {
        if (address is not null) dialog.FindControl<TextBox>("AddressBox")!.Text = address;
        if (user is not null) dialog.FindControl<TextBox>("UserBox")!.Text = user;
        if (password is not null) dialog.FindControl<TextBox>("PasswordBox")!.Text = password;
        if (name is not null) dialog.FindControl<TextBox>("NameBox")!.Text = name;
    }

    private static void Ok(ServerDialog dialog)
    {
        Click(dialog.FindControl<Button>("OkButton")!);
        AppHarness.Settle(100);
    }

    private static string Message(ServerDialog dialog) => dialog.FindControl<TextBlock>("Message")!.Text ?? "";

    [AvaloniaFact]
    public void Settings_has_a_Servers_section_that_says_where_passwords_go()
    {
        using var app = new AppHarness();
        app.Window.OpenSettings(SettingsSection.Servers);
        AppHarness.Settle(100);
        var panel = app.Window.FindControl<SettingsPanel>("SettingsPanel")!;

        Assert.Equal(SettingsSection.Servers, panel.Section);
        Assert.True(panel.FindControl<ScrollViewer>("ServersPage")!.IsVisible);
        Assert.True(app.ViewModel.Servers.HasNoServers);
        Assert.Contains("never in GZJam's settings or a backup", panel.FindControl<TextBlock>("PasswordsText")!.Text);
    }

    [AvaloniaFact]
    public void A_server_is_added_once_GZJam_has_signed_in_and_its_card_says_what_it_found()
    {
        using var app = new AppHarness();
        PretendServer(app);
        app.Window.OpenSettings(SettingsSection.Servers);
        var dialog = OpenDialog(app);
        Fill(dialog, address: "synology:4533", user: "gzjam", password: "sesame");
        Assert.Equal("synology", dialog.FindControl<TextBox>("NameBox")!.PlaceholderText);   // the name it gets if none is typed
        Ok(dialog);

        Assert.False(dialog.IsVisible);
        var server = Assert.Single(app.Database.GetServers());
        Assert.Equal(("synology", "http://synology:4533", "gzjam", true), (server.Name, server.Address, server.UserName, server.CanDownload));
        Assert.Equal("sesame", app.ViewModel.Servers.Passwords.Get(server.PasswordKey));

        var card = Assert.Single(app.ViewModel.Servers.Items);
        Assert.Equal("gzjam at http://synology:4533", card.SignIn);
        Assert.Equal("Navidrome 0.64.2 · 17,786 songs · downloads allowed", card.Status);
        Assert.False(card.IsProblem);
        AppHarness.Settle(50);
        var shown = app.Window.FindControl<SettingsPanel>("SettingsPanel")!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text);
        Assert.Contains("Navidrome 0.64.2 · 17,786 songs · downloads allowed", shown);
    }

    [AvaloniaFact]
    public void A_wrong_password_isnt_saved_and_the_dialog_says_why()
    {
        using var app = new AppHarness();
        PretendServer(app);
        var dialog = OpenDialog(app);
        Fill(dialog, address: "synology:4533", user: "gzjam", password: "open sesame", name: "Mine");
        Ok(dialog);

        Assert.True(dialog.IsVisible);
        Assert.Contains("not to that user name and password", Message(dialog));
        Assert.Empty(app.Database.GetServers());
        Assert.Equal("ADD", dialog.FindControl<Button>("OkButton")!.Content);   // no "anyway" for a wrong password
        dialog.Close();
    }

    [AvaloniaFact]
    public void A_server_that_doesnt_answer_can_be_added_anyway()
    {
        using var app = new AppHarness();
        PretendServer(app);
        var dialog = OpenDialog(app);
        Fill(dialog, address: "nowhere:4533", user: "gzjam", password: "sesame");
        Ok(dialog);
        Assert.Contains("Nothing answered", Message(dialog));
        Assert.Equal("ADD ANYWAY", dialog.FindControl<Button>("OkButton")!.Content);

        Ok(dialog);
        Assert.False(dialog.IsVisible);
        var server = Assert.Single(app.Database.GetServers());
        Assert.Null(server.CanDownload);   // not known until it answers
        Assert.StartsWith("Not signed in yet: nothing answered", Assert.Single(app.ViewModel.Servers.Items).Status);
    }

    [AvaloniaFact]
    public async Task A_server_that_doesnt_allow_downloads_is_shown_as_preview_only()
    {
        using var app = new AppHarness();
        PretendServer(app, canDownload: false);
        var result = (await app.ViewModel.Servers.SaveAsync(null, new ServerEdit("Dave's server", "dave:4533", "gzjam", "sesame")));

        Assert.True(result.Saved);
        var card = Assert.Single(app.ViewModel.Servers.Items);
        Assert.True(card.IsPreviewOnly);
        Assert.EndsWith("preview only", card.Status);
    }

    [AvaloniaFact]
    public async Task The_same_server_and_user_twice_is_refused()
    {
        using var app = new AppHarness();
        PretendServer(app);
        Assert.True((await app.ViewModel.Servers.SaveAsync(null, new ServerEdit("Mine", "synology:4533", "gzjam", "sesame"))).Saved);

        var again = (await app.ViewModel.Servers.SaveAsync(null, new ServerEdit("Again", "http://synology:4533/app/", "gzjam", "sesame")));
        Assert.False(again.Saved);
        Assert.Contains("already here, as Mine", again.Problem);
        Assert.Single(app.Database.GetServers());
    }

    [AvaloniaFact]
    public async Task Changing_a_server_keeps_its_password_when_left_blank_and_moves_it_with_a_new_address()
    {
        using var app = new AppHarness();
        PretendServer(app);
        var servers = app.ViewModel.Servers;
        Assert.True((await servers.SaveAsync(null, new ServerEdit("Mine", "synology:4533", "gzjam", "sesame"))).Saved);
        var card = Assert.Single(servers.Items);
        string oldKey = card.Server.PasswordKey;

        var dialog = OpenDialog(app, card);
        Assert.Equal("http://synology:4533", dialog.FindControl<TextBox>("AddressBox")!.Text);
        Fill(dialog, address: "192.168.190.25:4533", name: "My NAS");   // the password left blank
        Ok(dialog);

        Assert.False(dialog.IsVisible);
        var server = Assert.Single(app.Database.GetServers());
        Assert.Equal(("My NAS", "http://192.168.190.25:4533"), (server.Name, server.Address));
        Assert.Equal("sesame", servers.Passwords.Get(server.PasswordKey));
        Assert.Null(servers.Passwords.Get(oldKey));   // not left behind under the old address
    }

    [AvaloniaFact]
    public async Task Removing_a_server_asks_first_and_forgets_its_password()
    {
        using var app = new AppHarness();
        PretendServer(app);
        var servers = app.ViewModel.Servers;
        Assert.True((await servers.SaveAsync(null, new ServerEdit("Mine", "synology:4533", "gzjam", "sesame"))).Saved);
        var card = Assert.Single(servers.Items);
        string? asked = null;
        app.ViewModel.ConfirmAsync = (heading, _, _) =>
        {
            asked = heading;
            return Task.FromResult(true);
        };

        await servers.RemoveAsync(card);

        Assert.Equal("Remove Mine?", asked);
        Assert.Empty(app.Database.GetServers());
        Assert.True(servers.HasNoServers);
        Assert.Null(servers.Passwords.Get(card.Server.PasswordKey));
    }

    [AvaloniaFact]
    public async Task A_server_whose_password_isnt_on_this_computer_asks_to_sign_in_again()
    {
        using var app = new AppHarness();
        PretendServer(app);
        app.Database.AddServer("Mine", "http://synology:4533", "gzjam");   // as after restoring a backup on another computer
        app.ViewModel.Servers.Refresh();
        var card = Assert.Single(app.ViewModel.Servers.Items);

        await card.TestAsync();

        Assert.True(card.IsProblem);
        Assert.StartsWith("Sign in again", card.Status);
    }

    [AvaloniaFact]
    public async Task TEST_signs_in_again_and_remembers_what_it_found()
    {
        using var app = new AppHarness();
        PretendServer(app);
        var servers = app.ViewModel.Servers;
        servers.Passwords.Set(PasswordStore.KeyFor("http://synology:4533", "gzjam"), "sesame");
        app.Database.AddServer("Mine", "http://synology:4533", "gzjam");
        servers.Refresh();
        var card = Assert.Single(servers.Items);
        Assert.Equal("Not signed in yet.", card.Status);

        await card.TestAsync();

        Assert.Equal("Navidrome 0.64.2 · 17,786 songs · downloads allowed", card.Status);
        Assert.True(app.Database.GetServers()[0].CanDownload);
        servers.Refresh();   // as when Settings is opened again later: what the library remembers
        Assert.StartsWith("Navidrome 0.64.2 · downloads allowed · last reached", new ServerItemViewModel(servers, app.Database.GetServers()[0]).Status);
    }
}
