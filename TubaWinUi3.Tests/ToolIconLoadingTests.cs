using System.Drawing;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

[Collection("GlobalConfigTests")]
public sealed class ToolIconLoadingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "TubaIconTests_" + Guid.NewGuid().ToString("N"));
    private readonly string? _previousOverride = ConfigManager.DataDirectoryOverride;

    public ToolIconLoadingTests()
    {
        Directory.CreateDirectory(_root);
        ConfigManager.DataDirectoryOverride = _root;
    }

    [Fact]
    public async Task ConcurrentRequests_ReuseOneValidCachedImage()
    {
        var executable = Path.Combine(_root, "tool.exe");
        File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), executable);
        var paths = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => ToolIconService.ExtractIconToCacheAsync(executable)));

        var path = Assert.IsType<string>(paths[0]);
        Assert.All(paths, p => Assert.Equal(path, p));
        Assert.Single(Directory.GetFiles(Path.Combine(_root, "IconCache"), "*.png"));
        using var bitmap = new Bitmap(path);
        Assert.True(bitmap.Width > 0 && bitmap.Height > 0);
        var writeTime = File.GetLastWriteTimeUtc(path);
        Assert.Equal(path, await ToolIconService.ExtractIconToCacheAsync(executable));
        Assert.Equal(writeTime, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public async Task CancelledLoad_DoesNotPublishOrCreateCache()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var tool = CreateTool(Path.Combine(_root, "missing.exe"));
        await ToolIconService.LoadIconsAsync([tool], null, cts.Token);
        Assert.Null(tool.IconPath);
        Assert.False(Directory.Exists(Path.Combine(_root, "IconCache")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ToolIconService.ExtractIconToCacheAsync(tool.Path, cts.Token));
    }

    private static ToolItem CreateTool(string path) => new()
    {
        Name = "test", Category = "test", Path = path, RelativePath = path, Extension = ".exe"
    };

    public void Dispose()
    {
        ConfigManager.DataDirectoryOverride = _previousOverride;
        Directory.Delete(_root, recursive: true);
    }
}
