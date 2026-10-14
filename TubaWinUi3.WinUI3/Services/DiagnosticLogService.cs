namespace TubaWinUi3.Services;

/// <summary>诊断日志属于持久数据，不参与临时文件清理。</summary>
internal static class DiagnosticLogService
{
    internal static string DirectoryPath => Path.Combine(ConfigManager.GetDataDir(), "Logs");
    internal static string CrashLogPath => Path.Combine(DirectoryPath, "app_crash.log");
    internal static string StartupLogPath => Path.Combine(DirectoryPath, "startup_trace.log");

    internal static void Prepare()
    {
        Directory.CreateDirectory(DirectoryPath);
        foreach (var name in new[] { "app_crash.log", "startup_trace.log" })
        {
            try
            {
                var oldPath = Path.Combine(Path.GetTempPath(), name);
                if (!File.Exists(oldPath)) continue;
                File.AppendAllText(Path.Combine(DirectoryPath, name), File.ReadAllText(oldPath));
                File.Delete(oldPath);
            }
            catch { } // 迁移失败保留原文件，错误报告仍可收录。
        }
    }

    internal static void AppendCrash(string detail)
    {
        try
        {
            Prepare();
            File.AppendAllText(CrashLogPath, detail);
        }
        catch { }
    }
}
