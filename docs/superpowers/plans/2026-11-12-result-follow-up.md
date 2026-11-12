# AI 结果窗口连续追问实施计划

> **执行要求：**使用 `superpowers:executing-plans` 逐任务执行；每个任务先写失败测试，再写最小实现。

**目标：**在现有 AI 结果弹窗中增加多轮追问和修正能力，同时保留流式输出、复制、替换原文及正文划词功能。

**架构：**单窗口、单请求流、内存会话。`ResultWindow` 持有消息历史，`AiClient` 每次提交完整 `messages`；正文使用一个 Markdown 区域显示对话，底部增加追问输入区。

**技术栈：**.NET 8、C#、WPF、MdXaml、OpenAI 兼容 Chat Completions。

**Spec：**`docs/superpowers/specs/2026-11-12-result-follow-up-design.md`

## 全局约束

- 不引入 WebView2、新 UI 框架、持久化会话或额外 NuGet 依赖。
- 对话仅在当前窗口内存中保存，关闭窗口或发起新 AI 操作即丢弃。
- 首次发送追问后，当前窗口不再失焦自动关闭。
- 复制和替换仅使用最新一轮 AI 回答的渲染后纯文本。
- 请求历史内容累计上限为 262,144 个 UTF-16 字符；超限不截断历史。
- 新文案同时提供英语、简体中文和日语。
- 构建和测试输出保持在默认 `bin/`、`obj/`；截图放在 `artifacts/`。
- 不自动执行 Git 提交或发布。

## 重点审查项

1. 中文输入法确认候选时不能误发送 → Task 3 键盘测试。
2. 重试不能重复用户消息或遗留旧回答 → Task 3 重试测试。
3. 用户 Markdown、代码围栏不能破坏后续回答 → Task 2、3 渲染测试。
4. 复制/替换不能包含用户问题和历史回答 → Task 3 纯文本测试。
5. 停止、失败、空回答及历史超限不能制造错误上下文 → Task 3 状态测试。

---

### Task 1：AiClient 多轮消息

**文件：**`src/PopBar/AiClient.cs`、`tests/PopBar.Tests/Program.cs`

**接口：**

```csharp
internal sealed record ChatMessage(string Role, string Content);

public static Task Stream(
    Settings settings,
    IReadOnlyList<ChatMessage> messages,
    Action<string> append,
    CancellationToken token,
    HttpMessageHandler? handler = null,
    Action? onThinking = null);
```

- [ ] 扩展 `FakeHandler` 记录请求消息，增加多轮顺序测试并确认失败。
- [ ] 实现新重载，旧入口构造单条 `user` 后委托；再次运行测试确认通过。

### Task 2：Markdown 对话隔离

**文件：**`src/PopBar/MarkdownText.cs`、`tests/PopBar.Tests/Program.cs`

**接口：**

```csharp
internal static string FormatUserTurn(string label, string text);
internal static string FormatAssistantTurn(string label, string markdown);
```

- [ ] 增加用户 Markdown 转义和未闭合围栏测试并确认失败。
- [ ] 实现格式化，运行测试确认通过。

### Task 3：追问 UI 和多轮状态

**文件：**`src/PopBar/ResultWindow.cs`、`tests/PopBar.Tests/UiChecks.cs`

**接口：**

```csharp
internal delegate Task ConversationStreamer(
    Settings settings,
    IReadOnlyList<ChatMessage> messages,
    Action<string> append,
    CancellationToken token,
    Action? onThinking);

internal ResultWindow(
    Settings settings,
    PromptAction prompt,
    Selection selection,
    ConversationStreamer? streamer = null);
```

控件名称：`FollowUpInput`、`SendFollowUpButton`；保留现有结果控件名称。

- [ ] 增加多轮请求、重试、部分回答、键盘、历史上限和最新回答纯文本测试并确认失败。
- [ ] 增加追问 UI，实现消息历史和请求状态，运行测试确认通过。

### Task 4：自动关闭、本地化和隐私说明

**文件：**`src/PopBar/ResultWindow.cs`、三个 `Resources/Strings*.resx`、`README.md`、`tests/PopBar.Tests/UiChecks.cs`

- [ ] 增加“发送追问后不自动关闭”的失败测试。
- [ ] 实现对话状态对自动关闭的限制，补充三语文案，更新隐私与 README，运行测试确认通过。

### Task 5：完整验证

- [ ] `dotnet build src/PopBar -c Release`。
- [ ] `dotnet run --project tests/PopBar.Tests -c Release`。
- [ ] `./scripts/test-selection.ps1`。
- [ ] 生成 `artifacts/ui/` 截图并检查真实服务交互。

## 交互修订（2026-11-12）

- 默认隐藏追问输入区，不显示重试按钮。
- 结果完成后显示 `FollowUpButton`；点击后显示 `FollowUpComposer`，隐藏追问按钮并停用当前窗口自动关闭。
- `SendFollowUp()` 仅在追问模式下工作。
- 成功完成后清空底端状态文字，保留等待、流式、停止、历史超限和错误提示。
- 测试覆盖默认隐藏、追问替代重试、点击即停用自动关闭、完成后无状态文字。
