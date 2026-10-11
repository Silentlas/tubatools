using TubaWinUi3.Pages;

namespace TubaWinUi3.Services;

/// <summary>
/// 内置工具入口「AI 助手」：承载新的智能代理页面（AiAgentPage）。
/// 完整版 Agent 架构：多轮工具调用循环 + 步骤链可视化 + 确认卡片。
/// </summary>
public sealed class AiAssistantTool : IBuiltinTool
{
    public static async Task OpenWithContextAsync(string summary, Microsoft.UI.Xaml.XamlRoot root)
    {
        var preview = new Microsoft.UI.Xaml.Controls.ContentDialog
        {
            XamlRoot = root, Title = "发送给 AI 的检测摘要", PrimaryButtonText = "带入新对话", CloseButtonText = "取消",
            RequestedTheme = ThemeService.CurrentElementTheme,
            Content = new Microsoft.UI.Xaml.Controls.ScrollViewer
            {
                MaxHeight = 420, Content = ToolExperience.Text("确认以下内容。带入后可编辑，点击发送才会提交给所选提供商。\n\n" + summary)
            }
        };
        if (await preview.ShowAsync() != Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary) return;
        var page = new AiAgentPage();
        page.PrepareDiagnosticContext(summary);
        Open(page);
    }
    public string Id => "ai-assistant";
    public string Name => LocalizationService.L("Builtin_ai-assistant_Name", "AI 助手");
    public string Description => LocalizationService.L("Builtin_ai-assistant_Desc", "智能系统代理，可诊断问题、优化配置、读写文件、执行命令、联网搜索并执行操作。");
    public string Glyph => "\uE946";
    public string Category => "实用工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        var page = new AiAgentPage();
        Open(page);
        return Task.CompletedTask;
    }

    private static void Open(AiAgentPage page)
    {
        App.MainWindow?.NavigateToToolPage(typeof(ToolContentPage), new ToolContentPageParam
        {
            ToolId = "ai-assistant",
            Title = "AI 助手",
            Description = "智能系统代理，可诊断问题、优化配置、执行操作并联网搜索",
            Glyph = "\uE946",
            Content = page,
            OnClose = () => page.Unload()
        });

    }
}
