using System.Net;
using System.Text;
using GZJam.Library.Servers;

namespace GZJam.Library.Tests;

/// <summary>Music servers (#282): the Subsonic sign-in, what a check finds, and where servers and passwords are kept.</summary>
public sealed class ServerTests : IDisposable
{
    private readonly TempLibrary _lib = new();

    public void Dispose() => _lib.Dispose();

    // ───────────── Addresses ─────────────

    [Theory]
    [InlineData("synology:4533", "http://synology:4533")]
    [InlineData("  http://synology:4533/  ", "http://synology:4533")]
    [InlineData("https://music.example.com", "https://music.example.com")]
    [InlineData("https://music.example.com:443/", "https://music.example.com")]
    [InlineData("http://192.168.190.25:4533/app/#/album/5xVz", "http://192.168.190.25:4533")]   // a page of Navidrome's web player
    [InlineData("https://example.com/navidrome/app/", "https://example.com/navidrome")]             // behind a path
    [InlineData("http://synology:4533/rest/ping.view?u=x", "http://synology:4533")]                // the API itself
    [InlineData("HTTP://Synology:4533", "http://synology:4533")]
    [InlineData("http://example.com/apps", "http://example.com/apps")]                              // not the /app page
    public void An_address_typed_or_pasted_becomes_the_servers_own(string typed, string expected) =>
        Assert.Equal(expected, SubsonicClient.NormalizeAddress(typed));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ftp://synology")]
    [InlineData("my server")]
    [InlineData("http://")]
    public void Something_that_isnt_an_address_is_refused(string typed) =>
        Assert.Null(SubsonicClient.NormalizeAddress(typed));

    // ───────────── Signing in ─────────────

    [Fact]
    public void The_token_is_the_one_the_Subsonic_API_documents() =>
        Assert.Equal("26719a1196d2a940705a59634eb18eab", SubsonicClient.Token("sesame", "c19b2d"));

    [Fact]
    public async Task A_check_signs_in_with_a_token_and_never_sends_the_password()
    {
        var server = new FakeServer();
        var check = await server.Client("sesame").CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ServerCheckOutcome.Connected, check.Outcome);
        Assert.NotEmpty(server.Requests);
        foreach (Uri request in server.Requests)
        {
            var query = System.Web.HttpUtility.ParseQueryString(request.Query);
            Assert.Equal("gzjam", query["u"]);
            Assert.Equal(SubsonicClient.Token("sesame", query["s"]!), query["t"]);
            Assert.Null(query["p"]);
            Assert.DoesNotContain("sesame", request.ToString());
            Assert.Equal("GZJam", query["c"]);
            Assert.Equal("json", query["f"]);
        }
        Assert.Equal(server.Requests.Count, server.Requests.Select(r => System.Web.HttpUtility.ParseQueryString(r.Query)["s"]).Distinct().Count());   // a fresh salt each time
    }

    [Fact]
    public async Task A_check_finds_what_the_server_is_whether_it_allows_downloads_and_how_many_songs()
    {
        var check = await new FakeServer().Client("sesame").CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal("navidrome", check.ServerType);
        Assert.Equal("0.64.2", check.ServerVersion);   // the build in brackets left off
        Assert.Equal("Navidrome 0.64.2", check.ServerName);
        Assert.True(check.OpenSubsonic);
        Assert.True(check.CanDownload);
        Assert.Equal(17786, check.SongCount);
    }

    [Fact]
    public async Task A_server_that_doesnt_allow_downloads_says_so()
    {
        var check = await new FakeServer { DownloadRole = false }.Client("sesame").CheckAsync(TestContext.Current.CancellationToken);
        Assert.True(check.Connected);
        Assert.False(check.CanDownload);
    }

    [Fact]
    public async Task A_wrong_password_is_a_wrong_sign_in()
    {
        var check = await new FakeServer().Client("open sesame").CheckAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ServerCheckOutcome.WrongSignIn, check.Outcome);
        Assert.Equal("Navidrome 0.64.2", check.ServerName);   // it still said what it is
    }

    [Fact]
    public async Task A_server_that_cant_check_tokens_is_sent_the_password_the_older_way()
    {
        var server = new FakeServer { TokensWork = false };
        var check = await server.Client("sesame").CheckAsync(TestContext.Current.CancellationToken);

        Assert.True(check.Connected);
        var last = System.Web.HttpUtility.ParseQueryString(server.Requests[^1].Query);
        Assert.Equal("enc:" + Convert.ToHexStringLower(Encoding.UTF8.GetBytes("sesame")), last["p"]);
        Assert.Null(last["t"]);
    }

    [Fact]
    public async Task Something_that_isnt_a_Subsonic_server_is_named_as_such()
    {
        var server = new FakeServer { Answer = _ => (HttpStatusCode.OK, "<html><body>Router login</body></html>") };
        var check = await server.Client("sesame").CheckAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ServerCheckOutcome.NotSubsonic, check.Outcome);
    }

    [Fact]
    public async Task Nothing_answering_is_unreachable_not_a_crash()
    {
        var server = new FakeServer { Answer = _ => throw new HttpRequestException("No such host is known.") };
        var check = await server.Client("sesame").CheckAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ServerCheckOutcome.Unreachable, check.Outcome);
        Assert.Contains("No such host", check.Detail);
    }

    /// <summary>A Navidrome as the one tried on 2026-11-05 answers, with user "gzjam" and password "sesame".</summary>
    private sealed class FakeServer : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        public bool DownloadRole { get; init; } = true;
        public bool TokensWork { get; init; } = true;
        public Func<Uri, (HttpStatusCode, string)>? Answer { get; init; }

        public SubsonicClient Client(string password) =>
            new(new HttpClient(this), "http://synology:4533", "gzjam", password);

        private const string Head = """
            "status":"{0}","version":"1.16.1","type":"navidrome","serverVersion":"0.64.2 (10114574)","openSubsonic":true
            """;

        private static string Ok(string body = "") =>
            "{\"subsonic-response\":{" + string.Format(Head, "ok") + body + "}}";

        private static string Failed(int code, string message) =>
            "{\"subsonic-response\":{" + string.Format(Head, "failed") + $",\"error\":{{\"code\":{code},\"message\":\"{message}\"}}}}}}";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri uri = request.RequestUri!;
            Requests.Add(uri);
            (HttpStatusCode status, string body) = Answer?.Invoke(uri) ?? Respond(uri);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }

        private (HttpStatusCode, string) Respond(Uri uri)
        {
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            bool signedIn = query["t"] is { } token
                ? TokensWork && token == SubsonicClient.Token("sesame", query["s"]!)
                : query["p"] == "enc:" + Convert.ToHexStringLower(Encoding.UTF8.GetBytes("sesame"));
            if (query["t"] is not null && !TokensWork)
                return (HttpStatusCode.OK, Failed(41, "Token authentication not supported for LDAP users"));
            if (query["u"] != "gzjam" || !signedIn)
                return (HttpStatusCode.OK, Failed(40, "Wrong username or password"));
            return uri.AbsolutePath switch
            {
                "/rest/ping.view" => (HttpStatusCode.OK, Ok()),
                "/rest/getUser.view" => (HttpStatusCode.OK, Ok(
                    $",\"user\":{{\"username\":\"gzjam\",\"adminRole\":false,\"downloadRole\":{(DownloadRole ? "true" : "false")},\"streamRole\":true}}")),
                "/rest/getScanStatus.view" => (HttpStatusCode.OK, Ok(",\"scanStatus\":{\"scanning\":false,\"count\":17786,\"folderCount\":1162}")),
                _ => (HttpStatusCode.NotFound, "404 page not found"),
            };
        }
    }

    // ───────────── Kept in the library ─────────────

    [Fact]
    public void A_server_is_kept_in_the_library_without_its_password()
    {
        var db = _lib.Database;
        MusicServer added = db.AddServer("Navidrome (mine) ", "http://synology:4533", "gzjam");

        var server = Assert.Single(db.GetServers());
        Assert.Equal(added, server);
        Assert.Equal("Navidrome (mine)", server.Name);
        Assert.Null(server.CanDownload);   // not checked yet
        Assert.Equal("gzjam@http://synology:4533", server.PasswordKey);

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Assert.DoesNotContain("sesame", File.ReadAllText(db.DatabasePath, Encoding.Latin1));
    }

    [Fact]
    public void Several_servers_are_kept_and_the_same_one_twice_is_renamed_not_doubled()
    {
        var db = _lib.Database;
        db.AddServer("Mine", "http://synology:4533", "gzjam");
        db.AddServer("Dave's server", "https://dave.example.com", "jesse");
        db.AddServer("NAS", "http://synology:4533", "gzjam");

        Assert.Equal(["Dave's server", "NAS"], db.GetServers().Select(s => s.Name));
    }

    [Fact]
    public void What_a_check_found_is_kept_and_forgotten_when_the_address_changes()
    {
        var db = _lib.Database;
        long id = db.AddServer("Mine", "http://synology:4533", "gzjam").Id;
        var now = new DateTime(2026, 11, 5, 23, 0, 0, DateTimeKind.Utc);
        db.SaveServerCheck(id, new ServerCheck(ServerCheckOutcome.Connected, "navidrome", "0.64.2", true, CanDownload: false), now);

        var checkedServer = db.GetServer(id)!;
        Assert.False(checkedServer.CanDownload);
        Assert.Equal("navidrome", checkedServer.ServerType);
        Assert.Equal(now, checkedServer.CheckedUtc);

        db.SaveServerCheck(id, new ServerCheck(ServerCheckOutcome.Unreachable), now.AddHours(1));
        Assert.Equal(now, db.GetServer(id)!.CheckedUtc);   // a failed check doesn't undo what's known

        Assert.False(db.UpdateServer(id, "My NAS", "http://synology:4533", "gzjam").CanDownload);   // renamed: still known
        var moved = db.UpdateServer(id, "My NAS", "http://192.168.190.25:4533", "gzjam");
        Assert.Null(moved.CanDownload);
        Assert.Null(moved.CheckedUtc);
    }

    [Fact]
    public void A_removed_server_is_gone()
    {
        var db = _lib.Database;
        long id = db.AddServer("Mine", "http://synology:4533", "gzjam").Id;
        db.RemoveServer(id);
        Assert.Empty(db.GetServers());
    }

    // ───────────── Passwords ─────────────

    [Fact]
    public void Without_a_keychain_passwords_are_kept_in_a_file_only_its_user_can_read()
    {
        string path = Path.Combine(_lib.Directory, "data", FilePasswordStore.FileName);
        var store = new FilePasswordStore(path);
        Assert.Null(store.Get("gzjam@http://synology:4533"));

        store.Set("gzjam@http://synology:4533", "sesame");
        store.Set("jesse@https://dave.example.com", "pa\"ss wörd");
        Assert.Equal("sesame", new FilePasswordStore(path).Get("gzjam@http://synology:4533"));
        Assert.Equal("pa\"ss wörd", new FilePasswordStore(path).Get("jesse@https://dave.example.com"));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));

        store.Set("gzjam@http://synology:4533", "new one");
        Assert.Equal("new one", store.Get("gzjam@http://synology:4533"));
        store.Remove("gzjam@http://synology:4533");
        Assert.Null(store.Get("gzjam@http://synology:4533"));
        Assert.Equal("pa\"ss wörd", store.Get("jesse@https://dave.example.com"));
        Assert.False(store.IsKeychain);
    }

    /// <summary>
    /// This computer's own keychain, for real: Credential Manager on Windows; on Linux the Secret
    /// Service, or the file when the desktop has none (as on CI). A Mac's Keychain can ask
    /// permission in a window, so only with GZJAM_TEST_KEYCHAIN=1.
    /// </summary>
    [Fact]
    public void This_computers_keychain_keeps_and_forgets_a_password()
    {
        Assert.SkipWhen(OperatingSystem.IsMacOS() && Environment.GetEnvironmentVariable("GZJAM_TEST_KEYCHAIN") != "1",
            "The Keychain may ask in a window; set GZJAM_TEST_KEYCHAIN=1 to try it");
        var store = PasswordStore.ForThisComputer(Path.Combine(_lib.Directory, "data"));
        string key = PasswordStore.KeyFor($"http://gzjam-test-{Guid.NewGuid():N}.invalid", "tester");
        try
        {
            Assert.Null(store.Get(key));
            store.Set(key, "sesame wörd");
            Assert.Equal("sesame wörd", store.Get(key));
            store.Set(key, "changed");
            Assert.Equal("changed", store.Get(key));
            Assert.Equal(OperatingSystem.IsWindows() ? "Windows Credential Manager" : store.Where, store.Where);
        }
        finally
        {
            store.Remove(key);
        }
        Assert.Null(store.Get(key));
        store.Remove(key);   // twice is fine
    }
}
