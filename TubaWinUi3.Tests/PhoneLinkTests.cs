using TubaWinUi3.Services;
using Xunit;

namespace TubaWinUi3.Tests;

public class PhoneLinkTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PhoneLinkTests-" + Guid.NewGuid().ToString("N"));
    private readonly string? _previousOverride = ConfigManager.DataDirectoryOverride;
    private readonly Func<string, Task<bool>>? _previousApproval;

    public PhoneLinkTests()
    {
        AppSettings.Flush();
        ConfigManager.DataDirectoryOverride = _directory;
        AppSettings.InvalidateCache();
        _previousApproval = PhoneLinkService.PairApprovalHandler;
        PhoneLinkService.RevokeAll();
        PhoneLinkService.PairApprovalHandler = null;
    }

    public void Dispose()
    {
        PhoneLinkService.Stop();
        PhoneLinkService.RevokeAll();
        PhoneLinkService.PairApprovalHandler = _previousApproval;
        AppSettings.Flush();
        ConfigManager.DataDirectoryOverride = _previousOverride;
        AppSettings.InvalidateCache();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Fact]
    public void PairCode_RotationAndReloadPersistAcrossSettingsCacheReset()
    {
        var initial = PhoneLinkService.PairCode;
        PhoneLinkService.RefreshPairCode();
        var rotated = PhoneLinkService.PairCode;
        Assert.NotEqual(initial, rotated);
        AppSettings.InvalidateCache();
        Assert.Equal(rotated, PhoneLinkService.LoadPairCode());
        PhoneLinkService.RevokeAll();
        Assert.NotEqual(rotated, PhoneLinkService.PairCode);
        AppSettings.InvalidateCache();
        Assert.Equal(PhoneLinkService.PairCode, PhoneLinkService.LoadPairCode());
    }

    [Theory]
    [InlineData("000001")]
    [InlineData("123456")]
    public void PairCode_ValidSavedCodeIsReusedExactly(string saved)
    {
        AppSettings.Set(PhoneLinkService.PairCodeSettingKey, saved);
        AppSettings.Flush();
        AppSettings.InvalidateCache();
        Assert.Equal(saved, PhoneLinkService.LoadPairCode());
    }

    [Theory]
    [InlineData("123")]
    [InlineData("１２３４５６")]
    [InlineData("abcdef")]
    public void PairCode_InvalidSavedValueIsReplaced(string value)
    {
        AppSettings.Set(PhoneLinkService.PairCodeSettingKey, value);
        var code = PhoneLinkService.LoadPairCode();
        Assert.Matches(@"^[0-9]{6}$", code);
        AppSettings.InvalidateCache();
        Assert.Equal(code, AppSettings.Get(PhoneLinkService.PairCodeSettingKey));
    }

    [Fact]
    public async Task Pair_TemporaryTokenDoesNotBypassApproval_AndRevokeForgetsApproval()
    {
        var device = "test-" + Guid.NewGuid();
        Assert.NotNull(PhoneLinkService.TryPair(PhoneLinkService.PairCode, device).Token);
        Assert.False(PhoneLinkService.IsDeviceApproved(device));
        Assert.False(await PhoneLinkService.EnsureDeviceApprovedAsync(device));
        PhoneLinkService.PairApprovalHandler = _ => Task.FromResult(true);
        Assert.True(await PhoneLinkService.EnsureDeviceApprovedAsync(device));
        Assert.True(PhoneLinkService.IsDeviceApproved(device));
        PhoneLinkService.RevokeAll();
        Assert.False(PhoneLinkService.IsDeviceApproved(device));
        Assert.Empty(PhoneLinkService.PairedDeviceNames);
        Assert.Null(AppSettings.Get("PhoneLinkApprovedDevices"));
    }

    [Fact]
    public async Task Pair_RevokeDuringPendingApprovalCannotRestoreAuthorization()
    {
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        PhoneLinkService.PairApprovalHandler = _ => pending.Task;
        var approval = PhoneLinkService.EnsureDeviceApprovedAsync("pending");
        PhoneLinkService.RevokeAll();
        pending.SetResult(true);
        Assert.False(await approval);
        Assert.False(PhoneLinkService.IsDeviceApproved("pending"));
    }

    [Fact]
    public async Task Pair_CancellationRejectsPendingApproval()
    {
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        PhoneLinkService.PairApprovalHandler = _ => pending.Task;
        using var cts = new CancellationTokenSource();
        var approval = PhoneLinkService.EnsureDeviceApprovedAsync("cancelled", cts.Token);
        cts.Cancel();
        Assert.False(await approval.WaitAsync(TimeSpan.FromSeconds(2)));
        pending.SetResult(true);
        Assert.False(PhoneLinkService.IsDeviceApproved("cancelled"));
    }

    [Fact]
    public void Pair_FiveFailuresRotatePersistedCodeAndKeepLockout()
    {
        var old = PhoneLinkService.PairCode;
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        try
        {
            for (var i = 0; i < 5; i++) Assert.Null(PhoneLinkService.TryPair("wrong", "test").Token);
            Assert.NotEqual(old, PhoneLinkService.PairCode);
            AppSettings.InvalidateCache();
            Assert.Equal(PhoneLinkService.PairCode, PhoneLinkService.LoadPairCode());
            Assert.Contains("稍后", PhoneLinkService.TryPair(PhoneLinkService.PairCode, "test").Error!);
            PhoneLinkService.RefreshPairCode();
            PhoneLinkService.RevokeAll();
            Assert.Null(PhoneLinkService.TryPair(PhoneLinkService.PairCode, "test").Token);
        }
        finally
        {
            typeof(PhoneLinkService).GetField("_failedPairs", flags)!.SetValue(null, 0);
            typeof(PhoneLinkService).GetField("_lockedUntil", flags)!.SetValue(null, DateTime.MinValue);
        }
    }

    [Theory]
    [InlineData("TUBALINK_DISCOVER_V1", true)]
    [InlineData("TUBALINK_DISCOVER_V1\n", false)]
    [InlineData("tubalink_discover_v1", false)]
    [InlineData("", false)]
    [InlineData("TUBALINK_DISCOVER_V2", false)]
    public void Discovery_ProtocolIsExact(string request, bool valid) =>
        Assert.Equal(valid, PhoneLinkDiscovery.IsDiscoveryRequest(System.Text.Encoding.UTF8.GetBytes(request)));

    [Fact]
    public async Task Discovery_RealUdpReplyHasOnlyPublicMetadata_AndStopReleasesPort()
    {
        using var service = PhoneLinkDiscovery.TryStart(19876);
        Assert.NotNull(service);
        using var client = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
        var endpoint = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 18764);
        await client.SendAsync(System.Text.Encoding.UTF8.GetBytes(PhoneLinkDiscovery.Request), endpoint);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var reply = await client.ReceiveAsync(timeout.Token);
        Assert.Equal(18764, reply.RemoteEndPoint.Port);
        using var json = System.Text.Json.JsonDocument.Parse(reply.Buffer);
        var root = json.RootElement;
        Assert.Equal(new[] { "name", "port", "type", "version" }, root.EnumerateObject().Select(p => p.Name).OrderBy(n => n));
        Assert.Equal("tubalink", root.GetProperty("type").GetString());
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal(Environment.MachineName, root.GetProperty("name").GetString());
        Assert.Equal(19876, root.GetProperty("port").GetInt32());
        service.Dispose();
        await service.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        using var restarted = PhoneLinkDiscovery.TryStart(19877);
        Assert.NotNull(restarted);
    }

    [Fact]
    public void Discovery_PortBusyReturnsNullWithoutThrowing()
    {
        using var socket = new System.Net.Sockets.UdpClient(System.Net.Sockets.AddressFamily.InterNetwork);
        socket.ExclusiveAddressUse = true;
        socket.Client.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Any, 18764));
        Assert.Null(PhoneLinkDiscovery.TryStart(19876));
    }

    [AdminFact]
    public async Task Service_HttpRemainsAvailableWhenDiscoveryPortIsBusy_AndRestartKeepsCode()
    {
        using var socket = new System.Net.Sockets.UdpClient(System.Net.Sockets.AddressFamily.InterNetwork);
        socket.ExclusiveAddressUse = true;
        socket.Client.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Any, 18764));
        var code = PhoneLinkService.PairCode;
        PhoneLinkService.Start();
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };
        var ping = await http.GetStringAsync($"http://127.0.0.1:{PhoneLinkService.Port}/api/ping");
        Assert.Contains("TubaLink", ping);
        Assert.Equal(code, PhoneLinkService.PairCode);
        PhoneLinkService.Stop();
        PhoneLinkService.Start();
        Assert.Equal(code, PhoneLinkService.PairCode);
        Assert.Contains("TubaLink", await http.GetStringAsync($"http://127.0.0.1:{PhoneLinkService.Port}/api/ping"));
    }

    // HTTP.sys wildcard registration requires elevation, just like the real unpackaged app.
    private sealed class AdminFactAttribute : FactAttribute
    {
        public AdminFactAttribute()
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            if (!new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
                Skip = "Requires administrator: HTTP.sys http://+:port/ registration (access denied in non-elevated test host).";
        }
    }

    [Theory]
    [InlineData("Microsoft.PowerToys", true)]
    [InlineData("7zip.7zip", true)]
    [InlineData("Notepad++.Notepad++", true)]
    [InlineData("", false)]
    [InlineData("a b", false)]
    [InlineData("x; calc", false)]
    [InlineData("x\" --force", false)]
    public void WingetId_Validation(string id, bool ok) => Assert.Equal(ok, PhoneLinkService.IsValidWingetId(id));

    [Theory]
    [InlineData("..\\..\\evil.exe", "evil.exe")]
    [InlineData("a/b/c.txt", "c.txt")]
    [InlineData("", "file")]
    [InlineData(null, "file")]
    public void SanitizeFileName_StripsPaths(string? input, string expected) =>
        Assert.Equal(expected, PhoneLinkService.SanitizeFileName(input));

    [Fact]
    public void PairCode_IsSixDigits() => Assert.Matches(@"^\d{6}$", PhoneLinkService.NewPairCode());

    [Fact]
    public void Pair_WrongCode_Rejected()
    {
        var (token, error) = PhoneLinkService.TryPair("not-the-code", "test");
        Assert.Null(token);
        Assert.NotNull(error);
        PhoneLinkService.RevokeAll();
    }

    [Fact]
    public void Pair_CorrectCode_IssuesToken()
    {
        var (token, error) = PhoneLinkService.TryPair(PhoneLinkService.PairCode, "test");
        Assert.NotNull(token);
        Assert.Null(error);
        PhoneLinkService.RevokeAll();
    }

    [Fact]
    public void QrPayload_ContainsAddressAndCode()
    {
        var s = PhoneLinkService.BuildQrPayload("192.168.1.2");
        Assert.StartsWith("tubalink://192.168.1.2:", s);
        Assert.Contains("code=" + PhoneLinkService.PairCode, s);
    }

    [Fact]
    public void UploadLimit_IsTwentyGigabytes() =>
        Assert.Equal(20L * 1024 * 1024 * 1024, PhoneLinkService.MaxUploadBytes);

    [Fact]
    public void CancelJob_UnknownId_ReturnsFalse() => Assert.False(PhoneLinkService.CancelJob("not-a-job"));

    // ───────────────────────── Toast 点击参数解析 ─────────────────────────

    [Fact]
    public void ToastHandlerArgs_ParsesKeyValuePairs()
    {
        var (action, target, job) = PhoneLinkNotifier.ParseHandlerArgs(new[] { "action=phone-link", "target=jobs", "job=abc123" });
        Assert.Equal("phone-link", action);
        Assert.Equal("jobs", target);
        Assert.Equal("abc123", job);
    }

    [Fact]
    public void ToastHandlerArgs_DefaultsAndQuotes()
    {
        var (action, target, job) = PhoneLinkNotifier.ParseHandlerArgs(new[] { "action=phone-link", "target=chat" });
        Assert.Equal("phone-link", action);
        Assert.Equal("chat", target);
        Assert.Null(job);

        (action, target, job) = PhoneLinkNotifier.ParseHandlerArgs(new[] { "\"action=phone-link\"", "TARGET=CHAT" });
        Assert.Equal("phone-link", action);
        Assert.Equal("CHAT", target);
        Assert.Null(job);
    }

    [Fact]
    public void ToastHandlerArgs_RecognizesActiveInterceptFallback()
    {
        var (action, _, _) = PhoneLinkNotifier.ParseHandlerArgs(new[] { "-ToastActivated", "show-active-intercept" });
        Assert.Equal("show-active-intercept", action);
    }

    [Fact]
    public void ToastArgumentString_ParsesToolkitFormat()
    {
        var (action, target, job) = PhoneLinkNotifier.ParseArgumentString("action=phone-link&target=chat&job=xyz");
        Assert.Equal("phone-link", action);
        Assert.Equal("chat", target);
        Assert.Equal("xyz", job);

        (action, target, job) = PhoneLinkNotifier.ParseArgumentString(null);
        Assert.Equal("", action);
        Assert.Equal("", target);
        Assert.Null(job);
    }

    [Fact]
    public void Truncate_AddsEllipsis()
    {
        Assert.Equal("abc", PhoneLinkNotifier.Truncate("abc", 5));
        Assert.Equal("ab…", PhoneLinkNotifier.Truncate("abcdef", 2));
        Assert.Equal("", PhoneLinkNotifier.Truncate("", 3));
    }

    // ───────────────────────── 传输速度 ─────────────────────────

    [Fact]
    public void TransferReporter_InstantAndAverageSpeed()
    {
        var now = 0L;
        var events = new List<PhoneTransferProgress>();
        var reporter = new TransferSpeedReporter("t1", "a.bin", PhoneTransferDirection.Upload, 1000,
            e => events.Add(e), () => now, minIntervalMs: 100);

        now = 1000;
        reporter.Report(100); // 100 B / 1 s
        Assert.Single(events);
        Assert.Equal(100, events[0].BytesPerSecond, 3);
        Assert.Equal(1000, events[0].TotalBytes);
        Assert.Equal(PhoneTransferDirection.Upload, events[0].Direction);

        now = 1100;
        reporter.Report(150, force: true); // 50 B / 0.1 s
        Assert.Equal(500, events[1].BytesPerSecond, 3);

        now = 2000;
        reporter.Complete(1000); // 全程平均：1000 B / 2 s
        Assert.True(events[2].Completed);
        Assert.Equal(500, events[2].BytesPerSecond, 3);
    }

    [Fact]
    public void TransferReporter_ThrottlesAndStopsAfterFinish()
    {
        var now = 0L;
        var count = 0;
        var reporter = new TransferSpeedReporter("t2", "b.bin", PhoneTransferDirection.Download, -1,
            _ => count++, () => now, minIntervalMs: 200);

        now = 250;
        reporter.Report(10);
        Assert.Equal(1, count);

        now = 300;
        reporter.Report(20); // 距上次 50ms，被节流
        Assert.Equal(1, count);

        reporter.Fail(20);
        Assert.Equal(2, count);

        reporter.Report(30, force: true); // 结束后不再上报
        Assert.Equal(2, count);
    }

    [Fact]
    public void FormatSpeed_UsesReadableUnits()
    {
        Assert.Equal("512 B/s", PhoneTransferProgress.FormatSpeed(512));
        Assert.Equal("1.5 KB/s", PhoneTransferProgress.FormatSpeed(1536));
        Assert.Equal("2.0 MB/s", PhoneTransferProgress.FormatSpeed(2 * 1024 * 1024));
    }
}
