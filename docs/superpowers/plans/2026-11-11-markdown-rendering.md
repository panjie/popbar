# 结果窗口 Markdown 渲染 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 PopBar 结果窗口渲染 AI 返回的 Markdown，并让「复制」「替换原文」输出渲染后的纯文本。

**Architecture:** 在 `ResultWindow` 中用 MdXaml 的 `MarkdownScrollViewer` 替换只读 TextBox；对 SSE 流式原文做约 120ms 节流重渲染；用 `TextRange` 提取 FlowDocument 纯文本供复制和替换。

**Tech Stack:** C# / .NET 8 / WPF / MdXaml 1.27.0

**Spec:** `docs/superpowers/specs/2026-11-11-markdown-rendering-design.md`

## Global Constraints

- 目标框架 `net8.0-windows`，WPF，沿用现有紧凑 C# 风格，不添加注释。
- 新增依赖：MdXaml 1.27.0（MIT；传递依赖 MdXaml.Plugins、AvalonEdit）。
- single-file 发布仍是单个 exe。
- 「复制」「替换原文」输出渲染后的纯文本。
- 不改动 `AiClient`、提示词、选区验证逻辑。
- 用户未要求提交，计划不包含 git commit 步骤。

## Review Focus

- 流式输出出现未闭合代码围栏或截断 Markdown 时不能抛异常，已生成内容必须保留。
- 接近 256K 字符的大结果重渲染不能卡死或抛异常。
- 主题切换时已显示的结果窗口正文颜色和背景必须跟着变化。
- 流式期间或刚完成时复制/替换必须取到最新已渲染文本，不能残留旧内容。
- Markdown 链接只能通过安全浏览器命令打开，不能执行任意协议。

---

### Task 1: Markdown 纯文本提取与 MdXaml 依赖

**Files:**
- Modify: `src/PopBar/PopBar.csproj`
- Create: `src/PopBar/MarkdownText.cs`
- Test: `tests/PopBar.Tests/UiChecks.cs`

**Interfaces:**
- Produces: `internal static string MarkdownText.Plain(System.Windows.Documents.FlowDocument? document)`；`null` 返回空字符串，去掉末尾 `\r`/`\n`，保留段落间换行和代码缩进。

- [ ] **Step 1: 写失败测试**

在 `UiChecks.RunOnUiThread()` 的 `Check` 定义之后、主题检查之前插入：

```csharp
var plainDocument=new System.Windows.Documents.FlowDocument(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("第一段")));
plainDocument.Blocks.Add(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("第二段")));
Check(MarkdownText.Plain(plainDocument)=="第一段\r\n第二段","markdown plain text extraction");
Check(MarkdownText.Plain(null)=="","markdown plain text handles empty document");
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet run --project tests/PopBar.Tests -c Release`
Expected: 编译失败，`MarkdownText` 不存在。

- [ ] **Step 3: 添加 MdXaml 包**

Run: `dotnet add src/PopBar package MdXaml --version 1.27.0`
Expected: `src/PopBar/PopBar.csproj` 出现 `MdXaml` 1.27.0 的 `PackageReference`。

- [ ] **Step 4: 实现 `MarkdownText.Plain`**

`src/PopBar/MarkdownText.cs`：

```csharp
using System.Windows.Documents;

namespace PopBar;

internal static class MarkdownText
{
    internal static string Plain(FlowDocument? document)
    {
        if(document is null) return "";
        return new TextRange(document.ContentStart,document.ContentEnd).Text.TrimEnd('\r','\n');
    }
}
```

- [ ] **Step 5: 运行测试确认通过**

Run: `dotnet run --project tests/PopBar.Tests -c Release`
Expected: 全部 PASS，包含新增的 `markdown plain text extraction`。

- [ ] **Step 6: 检查改动范围**

Run: `git status --short`
Expected: 只有 `src/PopBar/PopBar.csproj`、`src/PopBar/MarkdownText.cs`、`tests/PopBar.Tests/UiChecks.cs` 及 NuGet 生成文件变化；不提交。

---

### Task 2: 结果窗口 Markdown 渲染

**Files:**
- Modify: `src/PopBar/ResultWindow.cs`
- Modify: `tests/PopBar.Tests/UiChecks.cs`
- Modify: `README.md:51`

**Interfaces:**
- Consumes: `MarkdownText.Plain(FlowDocument?)`（Task 1）。
- Produces: `ResultWindow` 内部控件 `MarkdownScrollViewer output`，`Name="OutputMarkdown"`；流式期间 `output.Markdown` 为当前渲染源，`output.Document` 为渲染结果。

- [ ] **Step 1: 写失败测试**

把 `UiChecks.cs` 结果窗口测试块中对 `TextBox` 的检查替换为：

```csharp
var output=FindNamed<MdXaml.MarkdownScrollViewer>(resultWindow,"OutputMarkdown");
// ...其余按钮查找保持不变...
Check(!output.IsToolBarVisible && output.IsSelectionEnabled && output.VerticalScrollBarVisibility==ScrollBarVisibility.Auto && output.HorizontalScrollBarVisibility==ScrollBarVisibility.Disabled,"result output is selectable scrolling markdown");
Check(output.ClickAction==MdXaml.ClickAction.SafetyOpenBrowser,"result links use safe browser command");
output.Markdown="# 标题\n\n**粗体** 与 `代码`\n\n- 第一项\n- 第二项\n\n```\nvar x = 1;\n```";
var rendered=output.Document!;
var renderedText=MarkdownText.Plain(rendered);
Check(rendered.Blocks.OfType<System.Windows.Documents.Paragraph>().Any(block=>Equals(block.Tag,"Heading1")),"markdown heading rendered");
Check(rendered.Blocks.OfType<System.Windows.Documents.List>().Any(),"markdown list rendered");
Check(rendered.Blocks.OfType<System.Windows.Documents.Paragraph>().Any(block=>Equals(block.Tag,"CodeBlock")),"markdown code block rendered");
Check(renderedText.Contains("标题") && renderedText.Contains("粗体") && renderedText.Contains("第一项") && renderedText.Contains("var x = 1;") && !renderedText.Contains("**") && !renderedText.Contains("##"),"markdown rendering and plain text");
output.Markdown="```\n未闭合代码";
Check(MarkdownText.Plain(output.Document!).Contains("未闭合代码"),"truncated markdown keeps content");
output.Markdown=string.Join("\n",Enumerable.Repeat("- 项目",2000));
Check(MarkdownText.Plain(output.Document!).Contains("项目"),"large markdown renders");
output.Markdown="# 解释\n\n「SCM」是一个缩写，常见含义包括：\n\n- **Source Code Management**：源代码管理\n- **Software Configuration Management**：软件配置管理\n\n```powershell\ngit status\n```";
```

保留原有的按钮、主题和 `Capture(resultWindow,"result")` 检查；把主题检查扩展为同时验证 `output.Background` 随主题变化。

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet run --project tests/PopBar.Tests -c Release`
Expected: 找不到 `OutputMarkdown`（当前仍是 `OutputTextBox`）。

- [ ] **Step 3: 修改 `ResultWindow` 字段与构造**

- 用 `readonly MdXaml.MarkdownScrollViewer output=new();` 替换 TextBox。
- 新增 `readonly StringBuilder markdown=new();` 和 `long lastRender;`。
- 设置 `output.Name="OutputMarkdown"`、`IsToolBarVisible=false`、`IsSelectionEnabled=true`、`VerticalScrollBarVisibility=Auto`、`HorizontalScrollBarVisibility=Disabled`、`ClickAction=ClickAction.SafetyOpenBrowser`。
- `output.Foreground` / `output.Background` 用 `SetResourceReference` 绑定 `TextBrush` / `SurfaceBrush`。
- 文档样式：基于 `MdXaml.MarkdownStyle.SasabuneStandard` 派生 `Style`，覆盖 `FlowDocument.FontFamily` 为 `Segoe UI Variable, Segoe UI, Microsoft YaHei UI`、`FontSize=14`，赋给 `output.MarkdownStyle`。
- 用 `Border`（`SurfaceBrush` 背景、`BorderBrush` 边框、`CornerRadius=6`、内边距约 `14,10,8,10`、外边距 `24,0,24,0`）包裹 `output`，再放入 Grid 第 1 行。

- [ ] **Step 4: 修改渲染、复制与替换**

- `Run()` 开始时清空 `markdown`、设置 `output.Markdown=""`、重置 `lastRender=0`。
- SSE 回调改为 `markdown.Append(chunk)`，并调用 `Render(false)`；`Render(force)` 在距上次渲染不足 120ms 且非强制时直接返回，否则把 `markdown.ToString()` 赋给 `output.Markdown` 并滚动到底部。
- 成功、取消、异常分支都调用 `Render(true)`，保留现有状态文案。
- 「复制」和「替换原文」改用 `MarkdownText.Plain(output.Document)`。
- `Render` 捕获渲染异常时回退为包含原文的普通 `FlowDocument`，并保证不抛到事件循环。
- 通过 `VisualTreeHelper` 查找 `MarkdownScrollViewer` 内部 `ScrollViewer`，调用 `ScrollToEnd()`；模板未应用时先 `ApplyTemplate()`。

- [ ] **Step 5: 运行测试确认通过**

Run: `dotnet run --project tests/PopBar.Tests -c Release`
Expected: 全部 PASS，包含新增的 markdown 检查。

- [ ] **Step 6: 生成视觉验收图**

Run: `$env:POPBAR_UI_ARTIFACTS="$PWD/artifacts/ui"; dotnet run --project tests/PopBar.Tests -c Release; Remove-Item Env:POPBAR_UI_ARTIFACTS`
Expected: `artifacts/ui/result-Light-100.png`、`result-Dark-150.png` 等显示渲染后的标题、粗体、列表和代码块，无 `##`、`**` 源码。

- [ ] **Step 7: 更新 README**

把 `README.md:51` 的「……系统朗读、Markdown 富文本渲染或开机自启设置」改为「……系统朗读或开机自启设置」。

- [ ] **Step 8: 完整验证**

Run: `dotnet build src/PopBar -c Release`
Run: `dotnet run --project tests/PopBar.Tests -c Release`
Run: `./scripts/publish.ps1`
Expected: 构建与测试通过；`dist/PopBar-win-x64-lite` 仍只包含单个 `PopBar.exe` 和 `README.md`，zip 生成成功。
