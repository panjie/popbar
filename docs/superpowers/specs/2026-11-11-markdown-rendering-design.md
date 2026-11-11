# 结果窗口 Markdown 渲染设计

日期：2026-11-11
状态：待批准

## 意图

结果窗口（`ResultWindow`）目前把 AI 返回的 Markdown 原样放进只读 TextBox，用户看到 `##`、`**`、`-` 等源码。希望结果按标题、粗体、列表、代码块等格式渲染，同时保留流式输出、复制和替换原文的能力。此设计推翻旧 UI 设计中「不做 Markdown 渲染」的非目标。

## 目标

- 结果窗口按 Markdown 渲染常见语法：标题、粗体/斜体、行内代码、代码块、列表、引用、链接、表格、分隔线。
- 保留现有流式生成体验：内容边生成边更新，完成后状态与按钮行为不变。
- 「复制」与「替换原文」输出渲染后的纯文本（所见即所得），不插入 `**`、`##` 等源码标记。
- 渲染结果跟随现有浅色/深色主题和字体；文本可选中，可滚动。
- 不改动 AI 请求、提示词、选区读取和替换确认逻辑。

## 非目标

- 不提供 Markdown 源码/渲染结果切换。
- 不做代码块语法高亮（统一等宽字体显示）。
- 不导出 HTML/PDF，不支持数学公式、图表、Mermaid 等扩展。
- 不引入 WebView2、Electron 或浏览器运行时。

## 方案选择

- **MdXaml（选用）**：成熟的 WPF Markdown → FlowDocument 库，MIT，支持 .NET 8。自带标题、列表、代码块、表格等渲染和随控件前景/背景色变化的样式。新增依赖 MdXaml 1.27.0、MdXaml.Plugins、AvalonEdit（均为 MIT）；single-file 发布仍是单个 exe，体积略增。
- **自己写 Markdown → FlowDocument 渲染器**：零依赖，但表格、嵌套列表、转义等边界需要长期维护，代码量约 200–300 行，不选。
- **Markdig.Wpf**：仓库已归档，依赖旧版 Markdig，不选。
- **WebView2 + HTML**：引入额外运行时和进程，违背项目轻量原生定位，不选。

## 设计

- 用 `MdXaml.MarkdownScrollViewer` 替换 `ResultWindow` 中的只读 TextBox，外层保留现有圆角 Surface 边框和 24px 左右边距。
- 设置 `IsToolBarVisible=false`、`IsSelectionEnabled=true`、自动纵向滚动、禁用横向滚动；链接通过 MdXaml 的安全命令用默认浏览器打开（非 http/https 链接先确认）。
- 文档样式基于 `MarkdownStyle.SasabuneStandard`，覆盖正文字体为应用现有字体（Segoe UI Variable / Segoe UI / Microsoft YaHei UI）、字号 14，代码使用 Cascadia Code / Microsoft YaHei UI。控件前景/背景通过资源引用绑定到 `TextBrush` / `SurfaceBrush`，让内置样式随主题更新。
- 流式渲染：保留完整 Markdown 原文；每约 120ms 最多重渲染一次，生成结束/取消/出错时再渲染最终版本；每次渲染后滚动到底部。重试时清空原文并重新开始。
- 纯文本提取：用 `TextRange(document.ContentStart, document.ContentEnd)` 取渲染后文本并去掉末尾空行。「复制」和「替换原文」都使用该文本。
- 关闭窗口时继续使用现有取消逻辑，不需要额外资源释放。

## 验证

- 更新 `tests/PopBar.Tests/UiChecks.cs` 的结果窗口检查：
  - 输出控件为 `MarkdownScrollViewer`，只读可选、无工具栏、纵向滚动。
  - 给控件设置包含标题、粗体、行内代码、列表、代码块的 Markdown 后，FlowDocument 中出现对应标题/代码块结构，且纯文本中不含 `**`、`##` 等标记。
  - 纯文本提取保留代码块内容、去掉末尾空行。
  - 浅色/深色主题切换后结果窗口背景变化，截图产物更新到 `artifacts/ui/`。
- 运行 `dotnet build src/PopBar -c Release` 和 `dotnet run --project tests/PopBar.Tests -c Release`。
- 手动启动应用，对一段真实 AI 输出检查流式渲染、复制、替换原文和链接点击。

## 留给实现

- 确认 MdXaml 自适应样式在运行时切换主题时能更新文档颜色；若不能，在主题切换后重新赋值 `Markdown` 触发重渲染。
- 确认 `MarkdownScrollViewer` 内部 `ScrollViewer` 的获取方式（模板应用后通过可视树查找），实现流式滚动到底部。
- 处理渲染异常：Markdown 解析失败时回退显示原文，避免窗口空白。
