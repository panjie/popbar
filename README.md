# PopBar

为 Windows 自用开发的原生划词工具条，交互参考 [panjie/omapop](https://github.com/panjie/omapop)。使用 C#、WPF、Win32 和 UI Automation 实现，不依赖 Electron、Python 或浏览器后台。

## 运行

解压 `PopBar-win-x64.zip`，双击 `PopBar.exe`。发布包自带 .NET 运行时，适用于 Windows 10/11 x64，无需管理员权限。首次启动显示设置窗口；关闭设置后仍在系统托盘运行。双击托盘图标打开设置，右键可暂停或退出。重复启动不会创建第二个监听进程。

1. 在支持辅助功能的应用中，拖动选中文字、双击选词或 Shift 点击扩展选区，鼠标附近会显示工具条。
2. 自动识别不到时，选中文字后按 **Ctrl + Alt + Space**。如果应用不提供选区，该快捷键会尝试临时 Ctrl+C 取词，并尽力还原原剪贴板。
3. 点击 **复制 / 剪切 / 粘贴 / 搜索 / 打开链接 / 文件位置**，或一个 AI 提示词按钮。
4. 点击工具条外、按键、滚动或切换窗口可收起工具条。选择时按住 Win 可抑制弹出。

剪切、粘贴仅在能够确认选区可编辑时出现。打开链接支持整段 HTTP(S) 地址和裸域名；文件位置支持完整存在的本地路径。默认搜索使用 Google，可在设置中改成包含 `{text}` 的 HTTPS 地址，例如 `https://www.bing.com/search?q={text}`。

终端默认需要按住 Shift 划词，且必须由终端公开选区；不会对终端执行 Ctrl+C 兜底，以免中断命令。工具条不抢输入焦点；首次 AI 预览或设置打开时会取得正常窗口焦点。

## AI 配置

在设置中填写：

- **API 基础地址**：如 `https://api.deepseek.com/v1`、`https://api.openai.com/v1`，或你使用的 OpenAI 兼容网关地址。不要填写最后的 `/chat/completions`。
- **模型 ID**：按服务商或本地服务提供的实际名称填写；不预设模型以免与账户权限不符。
- **API Key**：保存后以 Windows DPAPI 当前用户加密存储；留空保留已有密钥，勾选清除则删除。仅本机 HTTP 地址允许明文传输，远程服务需要 HTTPS。
- **提示词按钮**：默认翻译、解释、润色；可以修改、添加和删除，最多显示 12 个。`{{text}}` 原样替换为选中文字；未写占位符时选中文字附加到末尾。

点击按钮才发送请求。预览支持流式输出、停止、重试、复制、替换原文。重试会发起新请求，可能再次计费。不会自动复制或替换，停止/失败会保留部分文字并标记未完成。替换前激活原窗口，重新核对原控件、选区位置、文字和可编辑状态；不能确认时请手动复制。替换成功后结果保留在剪贴板中。

当前版本支持 **Chat Completions 兼容协议**，不是 OpenAI Responses、Claude 原生或 Gemini 原生协议。使用这些服务需要它们的兼容接口或兼容网关。预览当前以可选择的纯文本展示，Markdown 标记保留原样。

AI 请求整体限时 180 秒，输入上限 64K UTF-16 字符，输出上限 256K 字符，响应传输上限 4M 字符。不会记录选中文字或 AI 历史，不自动重试，不跟随接口重定向。

## 配置与兼容性

配置文件：`%LOCALAPPDATA%\PopBar\settings.json`。密钥加密值也在此文件中，仅同一个 Windows 用户能够解密；不要把此配置提交到 Git。默认排除常见密码管理器，密码框不读取；可以添加进程名排除应用。

Windows 没有统一的“主选区”。自动划词是否工作取决于应用提供的 UI Automation TextPattern。部分浏览器、Electron、PDF 阅读器、远程桌面、画布或管理员窗口可能不支持。快捷键兜底也要求应用支持 Ctrl+C，且剪贴板没有被其它软件占用。临时复制可能被系统剪贴板历史或第三方剪贴板工具记录，即使当前剪贴板随后恢复。

自动识别的 UIA 调用最多等待 1.5 秒，同时最多一个读取任务；异常提供程序卡住时其它取词会被忽略，可退出并重新启动 PopBar。快捷键被其它软件占用时会给出托盘提示。

首版未移植 PopClip 扩展市场、脚本扩展、长按空白处粘贴、系统朗读、Markdown 富文本渲染或开机自启设置。需要自启可自行把快捷方式放入 Windows 启动文件夹。

## 开发与验证

需要 .NET 8 SDK。

```powershell
dotnet build src/PopBar -c Release
dotnet run --project tests/PopBar.Tests -c Release
./scripts/test-selection.ps1
./scripts/publish.ps1
```

`PopBar.Tests` 使用内存模拟 HTTP 服务验证 SSE、中文编码、失败/截断/超限、取消、DPAPI 和请求格式，不访问真实 AI 服务。`test-selection.ps1` 会短暂打开自己的 WPF 测试窗口，通过真实 UI Automation 验证可编辑/只读/空/密码选区，以及原选区改变后的替换保护。

可手动启动测试窗口：`dotnet run --project tests/PopBar.Fixture`。建议在日常使用的浏览器、编辑器和 PDF 阅读器中分别试一次普通划词、快捷键取词和 AI 替换；也建议检查不同缩放比例的多显示器。真实服务调用需要用户自行配置，当前自动化测试未消耗 API 额度。

实现参考：[Windows TextPattern](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-textpattern-overview)、[Win32 鼠标钩子](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelmouseproc)、[Chat Completions 流事件](https://developers.openai.com/api/reference/resources/chat/subresources/completions/streaming-events)。
