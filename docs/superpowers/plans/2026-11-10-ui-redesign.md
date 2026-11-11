# PopBar UI Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 PopBar 的工具条、设置窗口和 AI 结果窗口重设计为支持亮色、暗色与跟随系统的 Windows 11 Fluent 风格，同时保持现有功能行为。

**Architecture:** 使用 `App.xaml` 中的共享 WPF 样式和可替换主题资源字典集中管理视觉；`ThemeManager` 负责主题解析与应用，`Ui` 保留轻量控件工厂。三个窗口继续拥有各自布局，设置窗口内部维护提示词草稿，业务逻辑和 Win32 交互不改。

**Tech Stack:** C#、WPF、.NET 8、Win32、现有控制台测试与 UI Automation fixture，不新增第三方依赖。

**Spec:** `docs/superpowers/specs/2026-11-10-ui-redesign-design.md`

## Global Constraints

- 保持 Windows 10/11 x64、.NET 8 和现有原生 WPF 架构，不引入第三方 UI 框架或运行时依赖。
- 不更改取词、全局快捷键、剪贴板、链接/路径识别、AI 协议、流式输出或替换原文的逻辑。
- 工具条保持不抢焦点、现有条件显示、最多 12 个提示词、换行及屏幕边界定位。
- 设置与结果窗口保留系统标题栏；不实现 Mica、亚克力或自绘标题栏。
- 主题值为“跟随系统 / 亮色 / 暗色”，旧配置默认跟随系统；保存后立即应用，跟随系统时响应 Windows 主题变化。
- 工具条默认仅显示图标，每个图标都有中文工具提示和无障碍名称。
- 提示词编辑使用窗口内草稿，取消不得修改已保存配置；保留非空校验和 12 项上限。
- AI 正文保持可选择的纯文本，不增加 Markdown 渲染。
- 不修改无关代码、测试断言或格式；未经用户明确要求不执行 Git 提交。

## Review Focus

- 旧配置缺少 `Theme` 或含未知主题值时不能因主题功能破坏启动；由 Task 1 的缺失/未知值测试和 Task 6 的启动检查覆盖。
- 两套主题缺少资源键或主题切换后旧窗口不更新会造成黑块/不可读文字；由 Task 2 的资源完整性与动态更新 UI 测试覆盖。
- 空提示词列表和 12 项上限容易让主从编辑器出现无效选择；由 Task 4 的空列表、添加/删除和上限 UI 测试覆盖。
- 取消编辑提示词不能提前改动原配置；由 Task 4 的草稿隔离 UI 测试覆盖。
- 只读选区、请求中/停止/失败状态和高 DPI/窄屏幕不能因视觉改造产生误操作或溢出；由 Task 5 的状态检查及 Task 6 的选区脚本和手动矩阵覆盖。

---

### Task 1: 主题偏好与无副作用解析

**Files:**
- Modify: `src/PopBar/Settings.cs`
- Create: `src/PopBar/Theme.cs`
- Modify: `src/PopBar/AssemblyInfo.Testing.cs`
- Test: `tests/PopBar.Tests/Program.cs`

**Interfaces:**
- Produces: `public enum AppTheme { System, Light, Dark }`。
- Produces: `Settings.Theme`（`AppTheme` 类型，默认 `AppTheme.System`，JSON 使用字符串值）。
- Produces: `internal static AppTheme ThemeManager.Resolve(AppTheme preference, bool systemUsesLight)`；`System` 根据布尔参数解析，其余值原样返回。
- Produces: `PopBar.Tests` 可访问程序集 internal 类型，供后续 UI 检查使用。

- [ ] **Step 1: 写失败测试**

在现有控制台测试中增加：反序列化不含 `Theme` 的 JSON 得到 `AppTheme.System`；`"Light"`、`"Dark"`、`"System"` 可字符串往返；未知字符串被 JSON 反序列化拒绝；`Resolve(System, true/false)` 分别返回 `Light/Dark`，显式主题不受系统布尔值影响。

- [ ] **Step 2: 运行测试并确认失败**

Run: `dotnet run --project tests/PopBar.Tests -c Release`

Expected: 编译失败，提示 `AppTheme` 或 `ThemeManager` 不存在。

- [ ] **Step 3: 实现主题模型和解析**

在 `Settings.cs` 定义枚举并用 `JsonStringEnumConverter` 序列化 `Theme`；在 `Theme.cs` 新建 `ThemeManager`，本任务只加入无副作用的 `Resolve`。在 `AssemblyInfo.Testing.cs` 增加 `InternalsVisibleTo("PopBar.Tests")`。

- [ ] **Step 4: 运行测试并确认通过**

Run: `dotnet run --project tests/PopBar.Tests -c Release`

Expected: 原有检查与新增主题检查全部 PASS，末尾计数增加且进程退出码为 0。

---

### Task 2: 共享主题资源、控件样式与运行时应用

**Files:**
- Create: `src/PopBar/Themes/Light.xaml`
- Create: `src/PopBar/Themes/Dark.xaml`
- Modify: `src/PopBar/App.xaml`
- Modify: `src/PopBar/Theme.cs`
- Modify: `src/PopBar/Ui.cs`
- Modify: `src/PopBar/App.xaml.cs`
- Create: `tests/PopBar.Tests/UiChecks.cs`
- Modify: `tests/PopBar.Tests/Program.cs`

**Interfaces:**
- Consumes: Task 1 的 `AppTheme`、`Settings.Theme`、`ThemeManager.Resolve`。
- Produces: `internal static bool ThemeManager.SystemUsesLightTheme()`，读取当前用户 Windows 应用主题，读取失败返回 `false`。
- Produces: `internal static void ThemeManager.Apply(AppTheme preference)`，替换应用主题资源字典，不创建或关闭窗口。
- Produces: 两套主题共同定义 `WindowBackgroundBrush`、`SurfaceBrush`、`SecondarySurfaceBrush`、`TextBrush`、`MutedTextBrush`、`BorderBrush`、`AccentBrush`、`AccentHoverBrush`、`AccentPressedBrush`、`OnAccentBrush`、`DangerBrush`、`HoverBrush`、`PressedBrush`、`DisabledBrush`、`FocusBrush`。
- Produces: `App.xaml` 共享样式键 `PrimaryButtonStyle`、`SecondaryButtonStyle`、`SubtleButtonStyle`、`DangerButtonStyle`、`ToolbarButtonStyle`、`NavigationButtonStyle`、`InputTextBoxStyle`、`InputPasswordBoxStyle`、`CheckBoxStyle`、`ComboBoxStyle`、`ToolTipStyle`，以及滚动条样式。
- Produces: 保留 `Ui.Style(Window)`、`Ui.Label(string,bool)`、`Ui.Input(string,bool)` 签名；`Ui.Button` 变为 `Ui.Button(string label, Action action, string styleKey = "SecondaryButtonStyle")`。
- Produces: `internal static Button Ui.GlyphButton(string glyph, string name, Action action)`，使用图标字体、`ToolbarButtonStyle`、工具提示与 `AutomationProperties.Name`。
- Produces: `internal static int UiChecks.Run()`，在独立 STA 线程建立 WPF `Application` 并返回新增通过数。

- [ ] **Step 1: 写失败的 UI 资源测试**

`UiChecks.Run` 检查两套主题的所有上述资源键均存在、同名窗口背景刷颜色不同；创建经 `Ui.Style` 处理的窗口后，从 Light 切换到 Dark 会更新该现有窗口背景，随后创建的新窗口也取得 Dark 背景。检查按钮、输入框、图标按钮引用正确样式，图标按钮工具提示和无障碍名称等于传入名称。由 `Program.cs` 将返回值加入总计数。

- [ ] **Step 2: 运行测试并确认失败**

Run: `dotnet run --project tests/PopBar.Tests -c Release`

Expected: 因 `UiChecks`、主题资源或 `GlyphButton` 尚不存在而编译失败。

- [ ] **Step 3: 建立主题字典和共享样式**

在两个主题文件中定义相同语义刷资源；在 `App.xaml` 合并默认 Light 字典并定义使用 `DynamicResource` 的控件模板/样式。样式覆盖正常、悬停、按下、禁用和焦点状态，字体使用 `Segoe UI Variable, Segoe UI, Microsoft YaHei UI`。图标字体使用 `Segoe Fluent Icons, Segoe MDL2 Assets`。保留系统标题栏。

- [ ] **Step 4: 实现主题应用和应用生命周期接线**

`ThemeManager.Apply` 用 `Resolve` 和 `SystemUsesLightTheme` 选择字典并替换应用资源。`App.OnStartup` 在读取设置、创建窗口前应用主题并订阅 `SystemEvents.UserPreferenceChanged`；仅当偏好为 `System` 时在 UI 线程重新应用。设置保存回调更新设置后立即应用新主题；`OnExit` 取消订阅。不得重建结果窗口或触碰 AI 请求对象。

- [ ] **Step 5: 更新共享控件工厂**

`Ui.Style` 对窗口背景和前景使用动态资源；`Ui.Label`、`Ui.Input` 和 `Ui.Button` 使用共享样式。暂时保留旧窗口编译所需成员，后续窗口任务逐步移除硬编码颜色。实现 `GlyphButton`，图标内容不显示操作文字，文字放入工具提示和无障碍名称。

- [ ] **Step 6: 运行测试并确认通过**

Run: `dotnet build src/PopBar -c Release && dotnet run --project tests/PopBar.Tests -c Release`

Expected: 构建成功，资源完整性、动态更新、新窗口继承、样式和图标无障碍检查全部 PASS。

---

### Task 3: 图标化划词工具条

**Files:**
- Modify: `src/PopBar/ToolbarWindow.cs`
- Modify: `src/PopBar/Ui.cs`（仅在共享图标工厂需要补充时）
- Test: `tests/PopBar.Tests/UiChecks.cs`

**Interfaces:**
- Consumes: `Ui.GlyphButton`、共享主题资源与样式。
- Preserves: `ToolbarWindow.Present(Selection selection, Native.Point point, Settings settings, Action configure, Action<PromptAction> ai, Action<string> notify)` 签名与所有操作回调。
- Produces: 可复制的提示词图标选择规则：名称为“翻译”“解释”“润色”时分别使用语言、信息、编辑图标，其他名称使用 Sparkle 图标。

- [ ] **Step 1: 写失败的工具条 UI 测试**

在 STA UI 检查中创建一个含“hello”的只读 `Selection` 和默认 `Settings`，调用 `Present` 后检查：复制、搜索、翻译、解释、润色、设置按钮的 `Content` 均为图标而非操作名称，`ToolTip` 和 `AutomationProperties.Name` 分别为“复制”“搜索”“翻译”“解释”“润色”“设置”；所有按钮 `Focusable == false`；组间存在分隔线；只读选区不出现剪切、粘贴和替换相关按钮。关闭测试工具条，不点击任何业务操作。

- [ ] **Step 2: 运行测试并确认失败**

Run: `dotnet run --project tests/PopBar.Tests -c Release`

Expected: 当前文字按钮无法满足图标内容/工具提示断言，测试以非零退出码失败。

- [ ] **Step 3: 重排工具条视觉内容**

保留 `Present` 中现有条件分支和异步操作主体，将 `Add` 改为接收图标和名称并调用 `Ui.GlyphButton`。按基础编辑、上下文操作、AI、设置分组插入细分隔线，空组不添加分隔线。工具条容器使用动态表面、边框、圆角和轻量阴影；保留 WrapPanel、宽度计算、ShowActivated、Win32 hook 和定位代码。

- [ ] **Step 4: 运行测试并检查定位构建**

Run: `dotnet build src/PopBar -c Release && dotnet run --project tests/PopBar.Tests -c Release`

Expected: 工具条图标、提示、分组、只读条件和不抢焦点属性检查全部 PASS。

- [ ] **Step 5: 手动检查图标工具条**

Run: `dotnet run --project src/PopBar -c Release`

Expected: 复制/剪切/粘贴/搜索/链接/路径/AI/设置按原条件出现；默认只有图标，悬停显示名称；100% 与高 DPI 下换行和屏幕边界定位正确，点击工具条不抢走原窗口输入焦点。

---

### Task 4: 三页设置导航与提示词主从编辑器

**Files:**
- Modify: `src/PopBar/SettingsWindow.cs`
- Test: `tests/PopBar.Tests/UiChecks.cs`

**Interfaces:**
- Consumes: `Settings.Theme`、共享样式、`Ui` 控件工厂。
- Preserves: `SettingsWindow(Settings settings, Action<Settings> saved)` 构造签名、保存时的 `next.Save()` 和现有校验规则。
- Produces: 导航/编辑控件名称 `GeneralNavigationButton`、`AiNavigationButton`、`PromptsNavigationButton`、`ThemeSelector`、`PromptList`、`PromptNameInput`、`PromptContentInput`、`AddPromptButton`、`DeletePromptButton`，供 UI 检查定位。

- [ ] **Step 1: 写失败的设置结构与草稿测试**

构造 `SettingsWindow` 并检查三个导航按钮和包含 `System/Light/Dark` 的主题选择器。切换导航时只显示对应页面。提示词页包含列表和详情编辑器；选择不同项更新名称/内容输入框。修改输入框后，原始 `settings.Prompts` 不变。测试添加后自动选中、删除后选择相邻项、删空后显示空状态且可继续添加，以及从空列表添加到 12 项后禁止继续添加。测试通过按钮 `RaiseEvent` 完成，不点击保存、不写真实配置文件。

- [ ] **Step 2: 运行测试并确认失败**

Run: `dotnet run --project tests/PopBar.Tests -c Release`

Expected: 当前平铺设置窗口缺少命名导航和主从编辑控件，测试失败。

- [ ] **Step 3: 实现设置页面骨架和常规/AI 页面**

使用左侧导航、右侧单页滚动内容和固定底部“取消 / 保存”。常规页放置外观、划词行为、搜索、排除应用及快捷键说明；AI 服务页放置接口、模型、密钥和清除选项。标签与帮助文字分离，保存按钮使用主样式，取消使用次级样式。主题选择器映射 `AppTheme` 三个值。

- [ ] **Step 4: 实现提示词草稿主从编辑**

构造时深拷贝名称和内容。列表选中项驱动右侧名称/多行内容编辑器；添加自动选中，删除选择相邻项，空列表显示空状态。添加在 12 项时不可用，删除只在有选中项时可用。保存时从草稿生成 `PromptAction` 列表，沿用搜索地址、AI 地址、名称/内容非空校验、密钥处理和 `next.Save()`；取消直接关闭且不回调保存。

- [ ] **Step 5: 运行测试并确认通过**

Run: `dotnet build src/PopBar -c Release && dotnet run --project tests/PopBar.Tests -c Release`

Expected: 三页导航、主题选项、草稿隔离、选择、添加/删除、空状态及 12 项上限检查全部 PASS。

- [ ] **Step 6: 手动检查设置保存和主题切换**

Run: `dotnet run --project src/PopBar -c Release`

Expected: 三页在最小窗口尺寸下可滚动且底部按钮固定；取消不保存；无效地址和空提示词仍显示原校验；保存后设置与所有已开窗口立即换主题；跟随系统时更改 Windows 应用主题会更新窗口且不打断结果内容。

---

### Task 5: AI 结果窗口统一视觉与状态层级

**Files:**
- Modify: `src/PopBar/ResultWindow.cs`
- Modify: `src/PopBar/Ui.cs`（移除不再使用的旧硬编码主题成员）
- Test: `tests/PopBar.Tests/UiChecks.cs`

**Interfaces:**
- Preserves: `ResultWindow(Settings settings, PromptAction prompt, Selection selection)` 构造签名。
- Preserves: `Run()` 的请求、流式追加、取消、重试、复制和替换逻辑及按钮启用规则。
- Produces: UI 检查用名称 `OutputTextBox`、`StatusText`、`StopButton`、`RetryButton`、`CopyResultButton`、`ReplaceButton`、`CloseResultButton`。

- [ ] **Step 1: 写失败的结果窗口结构测试**

用只读选区构造结果窗口但不触发 `Loaded`，检查紧凑标题、模型/字符数说明、正文、状态和操作区均存在；正文只读、可滚动且为纯文本 `TextBox`；停止/重试/复制/替换/关闭按钮引用正确样式，替换使用主样式。检查窗口背景使用动态资源，ThemeManager 切换后背景更新。

- [ ] **Step 2: 运行测试并确认失败**

Run: `dotnet run --project tests/PopBar.Tests -c Release`

Expected: 当前结果窗口缺少命名结构和新样式引用，测试失败。

- [ ] **Step 3: 重排结果窗口**

使用紧凑标题区、占据主要空间的正文表面、独立状态区和底部操作区。保留正文等宽字体回退、只读和滚动行为；主操作为替换，其余为次级/轻量样式。只移动现有事件处理，不改 `Run` 的请求和状态语义，不新增 Markdown 或新按钮。

- [ ] **Step 4: 运行自动验证**

Run: `dotnet build src/PopBar -c Release && dotnet run --project tests/PopBar.Tests -c Release && ./scripts/test-selection.ps1`

Expected: 构建成功；核心/UI 检查全部 PASS；选区脚本输出 6 个 PASS、无 FAIL，替换保护未回归。

- [ ] **Step 5: 手动检查结果状态**

Run: `dotnet run --project src/PopBar -c Release`

Expected: 请求中、完成、停止、失败和重试时按钮状态与原行为一致；只读选区不能替换，可编辑选区仍在替换前重新验证；长文本可滚动选择，窗口缩放不裁切状态或操作按钮。

---

### Task 6: 全量回归与视觉验收

**Files:**
- Modify: `README.md`（仅补充主题外观设置的位置和默认跟随系统行为）
- Verify: `src/PopBar`、`tests/PopBar.Tests`、`tests/PopBar.Fixture`

**Interfaces:**
- Consumes: Tasks 1–5 的全部实现。
- Produces: 可构建、通过现有回归且完成亮色/暗色人工验收的 PopBar。

- [ ] **Step 1: 更新最小使用说明**

在 `README.md` 的配置部分说明“设置 → 常规 → 外观”提供跟随系统、亮色、暗色，默认跟随系统；工具条图标通过悬停显示名称。不改写其他文档。

- [ ] **Step 2: 运行完整自动验证**

Run: `dotnet build src/PopBar -c Release && dotnet run --project tests/PopBar.Tests -c Release && ./scripts/test-selection.ps1`

Expected: 构建无错误；控制台报告全部检查通过；选区报告恰好 6 个 PASS、无 FAIL。

- [ ] **Step 3: 完成跨主题视觉验收**

Run: `dotnet run --project src/PopBar -c Release`

Expected: 逐项检查工具条、三个设置页面、提示词空/满列表和结果窗口的 Light/Dark/System；文字对比度、焦点、悬停、禁用、滚动、100% 与高 DPI、窄屏工具条换行均正常。旧配置首次打开为跟随系统，未知主题值沿用现有安全启动回退且不自动覆盖原文件。

- [ ] **Step 4: 检查最终差异范围**

Run: `git status --short && git diff --check && git diff --stat`

Expected: 只有本计划列出的 UI、主题、测试、设计和 README 文件发生变化；`git diff --check` 无空白错误；没有生成物、个人配置或密钥进入差异。

## 2026-11-11 接续验收记录

本记录反映实际执行结果；上面的历史步骤（包括先写失败测试等）不追溯勾选。

- Release 构建通过：0 warning、0 error。
- `dotnet run --project tests/PopBar.Tests -c Release`：63 checks passed。
- `scripts/test-selection.ps1`：6 PASS、0 FAIL，覆盖真实 UIA 选区和替换保护。
- `git diff --check` 通过，生成物位于已忽略的 `artifacts/`；没有改写用户配置或调用真实 AI 服务。
- 修复接续时的构建阻断：滚动条引用不存在的 `IncreaseLargeCommand`。补齐纵向反转和横向翻页命令。
- 修复主题下拉框模板未传递背景/边框、暗色未选复选框使用系统白底，以及提示词列表未统一主题的状态样式。
- 修复小窗口和长提示词的编辑区溢出：详情区可滚动，正文限制高度并独立滚动，底部保存/取消保持可见。
- 未知数字主题值安全回退到系统主题；替换按钮在请求完成前默认禁用。
- 新增对应的模板、主题回退、长正文/最小尺寸布局和替换初始状态回归检查。

### 可复现视觉产物

```powershell
$env:POPBAR_UI_ARTIFACTS = Join-Path (Get-Location) 'artifacts/ui'
dotnet run --project tests/PopBar.Tests -c Release
Remove-Item Env:POPBAR_UI_ARTIFACTS
```

使用 WPF 实际控件树生成 Light/Dark 下 100%/150% 的工具条、常规、AI 服务、提示词、空列表、最小窗口满列表和结果窗口 PNG，输出到 `artifacts/ui/`。已查看渲染图并修复上述外观/布局问题。结果窗口使用本地样本文本，不触发 Loaded 请求。

### 验证边界

150% PNG 为离屏高分辨率渲染，不等同于真实多显示器 DPI 迁移。Windows 系统主题广播、真实鼠标悬停/焦点、跨显示器边界定位及真实 AI 服务的完整结果窗口交互仍需实机验收；此记录不将这些项目标为通过。现有自动检查覆盖动态主题资源更新、系统/显式主题解析、工具条不激活属性、HTTP/SSE/取消逻辑和真实选区保护。代码审查由接续执行者完成，没有独立审查者结论。
