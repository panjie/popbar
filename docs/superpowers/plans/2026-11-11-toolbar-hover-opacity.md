# 工具条悬停透明度 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 工具条出现时半透明，鼠标移入时完全不透明，移出后恢复半透明。

**Architecture:** 在 `ToolbarWindow` 的可见根 `Border` 上做 `Opacity` 动画；`Present` 时停止动画并重置为空闲透明度。

**Tech Stack:** C# / .NET 8 / WPF

**Spec:** `docs/superpowers/specs/2026-11-11-toolbar-hover-opacity-design.md`

## Global Constraints

- 空闲透明度 0.35，悬停 1.0，动画时长 120ms，EaseOut。
- 只改变可见胶囊的透明度，不改变窗口定位、按钮、点击后隐藏行为。
- 不增加设置项。
- 用户未要求提交，计划不包含 git commit 步骤。

## Review Focus

- 每次 `Present` 后必须回到空闲透明度，不能残留上一次的悬停状态。
- 鼠标进入/离开必须启动新的透明度动画，快速进出不能卡在中间值。
- 半透明状态下按钮仍必须可点击（现有工具条点击测试覆盖）。
- 工具条刚好出现在鼠标下方时，没有鼠标移动可能保持半透明；这是可接受的降级，需手动确认。

---

### Task 1: 工具条悬停透明度

**Files:**
- Modify: `src/PopBar/ToolbarWindow.cs`
- Test: `tests/PopBar.Tests/UiChecks.cs`

**Interfaces:**
- Produces: `internal const double ToolbarWindow.IdleOpacity = 0.35`；根 `Border` 的 `Name = "ToolbarSurface"`；`Present` 重置透明度。

- [ ] **Step 1: 写失败测试**

在 `UiChecks` 工具条测试块中，保存/恢复光标并把光标移到工具条外，然后断言：

```csharp
var toolbarCursor=System.Windows.Forms.Cursor.Position;
System.Windows.Forms.Cursor.Position=new System.Drawing.Point(0,0);
try {
    toolbar.Present(new Selection(0,"hello",false,null,null),new Native.Point { X=100,Y=100 },new Settings(),()=>{},_=>{},_=>{});
    var toolbarSurface=(Border)toolbar.Content;
    Check(toolbarSurface.Name=="ToolbarSurface" && Math.Abs(toolbarSurface.Opacity-ToolbarWindow.IdleOpacity)<0.001,"toolbar starts semi-transparent");
    // ...现有按钮/截图检查...
    toolbarSurface.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0) { RoutedEvent=System.Windows.Input.Mouse.MouseEnterEvent });
    Check(toolbarSurface.HasAnimatedProperties,"toolbar hover fades in");
    toolbarSurface.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0) { RoutedEvent=System.Windows.Input.Mouse.MouseLeaveEvent });
    Check(toolbarSurface.HasAnimatedProperties,"toolbar leave fades out");
    // ...第二次 Present...
    Check(!toolbarSurface.HasAnimatedProperties && Math.Abs(toolbarSurface.Opacity-ToolbarWindow.IdleOpacity)<0.001,"toolbar presentation resets idle opacity");
} finally { toolbar.Close(); System.Windows.Forms.Cursor.Position=toolbarCursor; }
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet run --project tests/PopBar.Tests -c Release`
Expected: 失败于 `toolbar starts semi-transparent`（当前透明度为 1，且根 Border 未命名）。

- [ ] **Step 3: 修改 `ToolbarWindow`**

- 根 `Border` 提升为字段 `readonly Border surface`，设置 `Name="ToolbarSurface"`、`Opacity=IdleOpacity`。
- 添加 `internal const double IdleOpacity=0.35;`。
- `surface.MouseEnter` 调用 `Fade(1)`，`surface.MouseLeave` 调用 `Fade(IdleOpacity)`。
- `Fade(double target)` 使用 `DoubleAnimation(target,TimeSpan.FromMilliseconds(120))`，`QuadraticEase` EaseOut，作用于 `UIElement.OpacityProperty`。
- `Present` 开头执行 `surface.BeginAnimation(UIElement.OpacityProperty,null); surface.Opacity=IdleOpacity;`。

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet run --project tests/PopBar.Tests -c Release`
Expected: 工具条检查通过；完整套件仍可能停在仓库原有的前台窗口检查（基线同样失败）。如需要，临时绕过该检查确认其余 107+ 项通过。

- [ ] **Step 5: 生成视觉验收图**

Run: `$env:POPBAR_UI_ARTIFACTS="$PWD/artifacts/ui"; dotnet run --project tests/PopBar.Tests -c Release; Remove-Item Env:POPBAR_UI_ARTIFACTS`
Expected: `artifacts/ui/toolbar-*.png` 显示半透明工具条。

- [ ] **Step 6: 完整验证**

Run: `dotnet build src/PopBar -c Release`
Run: `./scripts/publish.ps1`
Expected: 构建、单文件发布通过。
