using PopBar;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

var passed=0;
void Check(bool yes,string name) { if(!yes) throw new Exception(name); Console.WriteLine("PASS "+name); passed++; }
async Task Throws(Func<Task> action,string name) { try { await action(); } catch(Exception) { Check(true,name); return; } throw new Exception("Expected failure: "+name); }
System.Globalization.CultureInfo culture(string name) => System.Globalization.CultureInfo.GetCultureInfo(name);
Check(UiText.Resolve(UiLanguage.System,culture("ja-JP")).Name=="ja-JP"
    && UiText.Resolve(UiLanguage.System,culture("zh-TW")).Name=="zh-CN"
    && UiText.Resolve(UiLanguage.System,culture("fr-FR")).Name=="en-US", "Windows display language resolves to supported UI cultures");
Check(UiText.Resolve(UiLanguage.English,culture("ja-JP")).Name=="en-US", "manual language overrides Windows display language");
Check(JsonSerializer.Deserialize<Settings>("{}")!.Language==UiLanguage.System &&
    JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(new Settings { Language=UiLanguage.Japanese }))!.Language==UiLanguage.Japanese,
    "UI language defaults to Windows and persists a manual selection");
var resources=new System.Resources.ResourceManager("PopBar.Resources.Strings",typeof(UiText).Assembly);
var englishResources=resources.GetResourceSet(System.Globalization.CultureInfo.InvariantCulture,true,false)!;
foreach(var name in new[]{"ja","zh-Hans"}) {
    var translated=resources.GetResourceSet(culture(name),true,false);
    Check(translated!=null && englishResources.Cast<System.Collections.DictionaryEntry>().All(entry=>
        translated.GetObject((string)entry.Key) is string { Length: >0 }),$"complete UI translations for {name}");
}
UiText.Apply(UiLanguage.Chinese);
Check(Actions.SearchUri("https://example.com/?q={text}","中文 &x=#").Query=="?q=%E4%B8%AD%E6%96%87%20%26x%3D%23","search query encoding");
Check(Actions.Link("example.com/a?q=中文")?.Host=="example.com","bare domain");
Check(Actions.Link("javascript:alert(1)")==null && Actions.Link("file:///C:/test.exe")==null,"unsafe schemes rejected");
Check(Actions.Link("https://user:password@example.com")==null,"URL credentials rejected");
Check(Actions.Link("some words example.com")==null,"phrase remains search");
Check(new[]{"https://example.com/docs","example.com","打开：https://example.com/docs"}.All(text=>SmartActions.Detect(text)==null),"URLs are excluded from smart actions");
Check(SmartActions.Detect("https://a.com https://b.com")==null,"ambiguous URLs do not select an arbitrary target");
Check(SmartActions.Detect("A long paragraph mentioning https://a.com with much more unrelated text stays a search")==null,"incidental URL is not dominant");
Check(SmartActions.Detect("hello world")==null && SmartActions.Detect("javascript:alert(1)")==null,"ordinary prose and unsupported schemes have no action");
var smartCommand=SmartActions.Detect("git clone https://example.com/repository.git");
Check(smartCommand is { Kind:SmartActionKind.Command,PowerShell:false } && smartCommand.StartInfo().Arguments=="/d /k git clone https://example.com/repository.git","command arguments are not mistaken for URL actions");
Check(SmartActions.Detect(@"PS C:\work> Get-ChildItem -Force") is { Target:"Get-ChildItem -Force",PowerShell:true },"PowerShell prompt removed");
Check(SmartActions.Detect(@"C:\work> dir /b") is { Target:"dir /b",PowerShell:false },"CMD prompt removed");
Check(SmartActions.Detect("```powershell\nWrite-Output '你好 & $x'\n```") is { Target:"Write-Output '你好 & $x'",PowerShell:true },"single-line fenced command recognized");
var psAction=SmartActions.Detect("Get-ChildItem 'C:\\Folder with spaces'")!;
Check(Encoding.Unicode.GetString(Convert.FromBase64String(psAction.StartInfo().ArgumentList[^1]))==psAction.Target,"PowerShell transport preserves literal command text");
Check(SmartActions.Detect("git status\ndel important.txt")==null,"multiple command lines are not silently executed");
var smartFixture=Path.Combine(Path.GetTempPath(),"PopBar smart "+Guid.NewGuid().ToString("N")+".txt");
try {
    File.WriteAllText(smartFixture,"fixture");
    Check(SmartActions.Detect(smartFixture)==null && SmartActions.Detect(Path.GetDirectoryName(smartFixture)!)==null,"files and folders are excluded from smart actions");
    Check(Actions.ExistingPath(smartFixture)==smartFixture,"existing file location action is preserved");
    Check(SmartActions.Detect(smartFixture+".missing")==null,"missing file is not offered");
} finally { File.Delete(smartFixture); }
Check(MarkdownText.DemoteImages("![追踪](https://example.com/a.png)")=="[追踪](https://example.com/a.png)","markdown images become links");
Check(MarkdownText.DemoteImages("普通文字")=="普通文字","markdown without images unchanged");
var userTurn=MarkdownText.FormatUserTurn("你","# 标题\n**粗体** <b>x</b> ![图](https://example.com/a.png)");
Check(userTurn.StartsWith("### 你\n\n> ") && userTurn.Contains(@"\# 标题") &&
    userTurn.Contains(@"\*\*粗体\*\*") && userTurn.Contains("&lt;b&gt;x&lt;/b&gt;") &&
    !userTurn.Contains("\n# 标题") && !userTurn.Contains("![图]"),
    "follow-up questions are escaped to plain text");
Check(MarkdownText.FormatAssistantTurn("AI","```cs\nvar x = 1;")=="### AI\n\n```cs\nvar x = 1;\n```\n\n",
    "unclosed assistant code fence is closed for display");
Check(MarkdownText.FormatAssistantTurn("AI","~~~\nx\n~~~")=="### AI\n\n~~~\nx\n~~~\n\n",
    "closed assistant code fence is left unchanged");
Check(AiClient.Endpoint("http://127.0.0.1:1234/v1").AbsoluteUri=="http://127.0.0.1:1234/v1/chat/completions","local endpoint");
await Throws(()=>Task.FromResult(AiClient.Endpoint("http://example.com/v1")),"remote plaintext rejected");
await Throws(()=>Task.FromResult(AiClient.Endpoint("https://example.com/v1?key=x")),"query credentials rejected");
Check(AiClient.BuildPrompt("Translate {{text}}","{{text}} $x `test`")=="Translate {{text}} $x `test`","prompt substitution is literal");
Check(AiClient.BuildPrompt("Explain","hello")=="Explain\n\nhello","prompt append");
var settings=new Settings(); settings.SetKey("test-key-not-real");
Check(settings.GetKey()=="test-key-not-real"&&!settings.EncryptedKey.Contains("test-key-not-real"),"DPAPI round trip");
Check(settings.Excludes("KeePassXC")&&!settings.Excludes("notepad"),"process exclusions");
var startupTestKey=$@"Software\PopBar.StartupTest.{Guid.NewGuid():N}";
var startup=new StartupRegistration(startupTestKey);
const string startupExe=@"C:\Example Folder\PopBar.exe";
try {
    Check(!startup.IsEnabled(),"login startup is off with no registration");
    startup.SetEnabled(true,startupExe);
    using(var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(startupTestKey,writable:true)!) {
        Check(startup.IsEnabled() && Equals(key.GetValue("PopBar"),$"\"{startupExe}\" --startup"),
            "login startup registers a quoted executable path");
        key.SetValue("UnrelatedApp","keep");
    }
    startup.SetEnabled(false,null);
    using var remaining=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(startupTestKey);
    Check(!startup.IsEnabled() && Equals(remaining?.GetValue("UnrelatedApp"),"keep"),
        "disabling login startup removes only PopBar's registration");
} finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKey(startupTestKey,throwOnMissingSubKey:false); }
Check(JsonSerializer.Deserialize<Settings>("{}")!.ToolbarPosition==ToolbarPlacement.Above,"legacy toolbar stays above");
Check(JsonSerializer.Deserialize<Settings>("{}")!.ToolbarIdleOpacity==35,"toolbar idle opacity defaults for existing settings");
Check(JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(new Settings { ToolbarIdleOpacity=65 }))!.ToolbarIdleOpacity==65,
    "toolbar idle opacity persists");
Check(ToolbarWindow.NormalizeIdleOpacity(0)==10 && ToolbarWindow.NormalizeIdleOpacity(150)==100,
    "toolbar idle opacity is bounded");
Check(JsonSerializer.Deserialize<Settings>("{}")!.AutoCloseResultOnDeactivate,"result auto-close defaults on for existing settings");
var manualCloseSettings=JsonSerializer.Serialize(new Settings { AutoCloseResultOnDeactivate=false });
Check(!JsonSerializer.Deserialize<Settings>(manualCloseSettings)!.AutoCloseResultOnDeactivate,"disabled result auto-close survives saving and reloading");
Check(Enum.GetValues<ToolbarPlacement>().All(position=>JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(new Settings { ToolbarPosition=position }))!.ToolbarPosition==position),"toolbar placement JSON round trip");
var defaultAppPositions=new Settings();
Check(defaultAppPositions.ToolbarAppPositions.Count==10 && defaultAppPositions.ToolbarPositionFor("MS-TEAMS")==ToolbarPlacement.Below &&
    defaultAppPositions.ToolbarPositionFor("OUTLOOK")==ToolbarPlacement.Below && defaultAppPositions.ToolbarPositionFor("msedgewebview2")==ToolbarPlacement.Above,
    "app placement presets are active out of the box and match host aliases");
var appPositionSettings=JsonSerializer.Deserialize<Settings>("""
    {"ToolbarAppPositions":[
        {"Executables":["MS-TEAMS.EXE","Teams.exe"],"Position":"Above"},
        {"Executables":["notepad.exe"],"Position":"Above"}
    ]}
    """)!;
Check(appPositionSettings.ToolbarAppPositions.Count==11 && appPositionSettings.ToolbarPositionFor("MS-TEAMS")==ToolbarPlacement.Above &&
    appPositionSettings.ToolbarPositionFor("Teams")==ToolbarPlacement.Above && appPositionSettings.ToolbarPositionFor("olk")==ToolbarPlacement.Below &&
    JsonSerializer.Deserialize<Settings>("{}")!.ToolbarPositionFor("Teams")==ToolbarPlacement.Below &&
    JsonSerializer.Deserialize<Settings>("{\"ToolbarAppPositions\":[]}")!.ToolbarPositionFor("Teams")==ToolbarPlacement.Below,
    "older settings gain missing presets and keep previously configured positions");
appPositionSettings.ToolbarAppPositions.RemoveAll(rule=>rule.Executables.Contains("olk.exe"));
var reloadedAppPositions=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(appPositionSettings))!;
Check(reloadedAppPositions.ToolbarAppPositions.Count==10 && reloadedAppPositions.ToolbarPositionFor("olk")==ToolbarPlacement.Above &&
    reloadedAppPositions.ToolbarPositionFor("winword")==ToolbarPlacement.Below &&
    reloadedAppPositions.ToolbarAppPositions.Any(rule=>rule.Executables.Contains("notepad.exe")),
    "removing a preset persists through reload alongside custom rules");
Check(ToolbarWindow.VerticalPosition(400,50,0,1000,ToolbarPlacement.Above)==336 && ToolbarWindow.VerticalPosition(400,50,0,1000,ToolbarPlacement.Below)==420,"toolbar preferred side");
Check(ToolbarWindow.VerticalPosition(20,50,0,1000,ToolbarPlacement.Above)==40 && ToolbarWindow.VerticalPosition(980,50,0,1000,ToolbarPlacement.Below)==916,"toolbar flips at screen edges");
Check(ToolbarWindow.VerticalPosition(-500,75,-1080,0,ToolbarPlacement.Below)==-480 && ToolbarWindow.VerticalPosition(40,200,0,100,ToolbarPlacement.Below)==0,"toolbar negative monitor and oversized bounds");
Check(JsonSerializer.Deserialize<Settings>("{}")!.Theme==AppTheme.System,"legacy settings follow system theme");
Check(JsonSerializer.Serialize(new Settings { Theme=AppTheme.Light }).Contains("\"Theme\":\"Light\"") && JsonSerializer.Serialize(new Settings { Theme=AppTheme.Dark }).Contains("\"Theme\":\"Dark\"") && JsonSerializer.Serialize(new Settings { Theme=AppTheme.System }).Contains("\"Theme\":\"System\""),"theme preference uses string JSON values");
Check(Enum.GetValues<AppTheme>().All(theme=>JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(new Settings { Theme=theme }))!.Theme==theme),"theme preference round trip");
await Throws(()=>Task.FromResult(JsonSerializer.Deserialize<Settings>("{\"Theme\":\"Sepia\"}")),"unknown theme rejected");
Check(ThemeManager.Resolve(AppTheme.System,true)==AppTheme.Light && ThemeManager.Resolve(AppTheme.System,false)==AppTheme.Dark,"system theme resolution");
Check(ThemeManager.Resolve(AppTheme.Light,false)==AppTheme.Light && ThemeManager.Resolve(AppTheme.Dark,true)==AppTheme.Dark,"explicit theme resolution");
var sse="data: {\"choices\":[{\"delta\":{\"content\":\"你好\"}}]}\r\n\r\ndata: {\"choices\":[{\"delta\":{\"content\":\" world\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n";
var result=new StringBuilder(); await AiClient.ParseStream(new StringReader(sse),x=>result.Append(x),default);
Check(result.ToString()=="你好 world","SSE unicode CRLF and completion");
var reasoningSse="""
data: {"choices":[{"delta":{"content":"","reasoning_content":"first"}}]}

data: {"choices":[{"delta":{"reasoning_content":"second"}}]}

data: {"choices":[{"delta":{"content":"答案"},"finish_reason":"stop"}]}

data: [DONE]
"""+"\n\n";
var phases=new List<string>();
await AiClient.ParseStream(new StringReader(reasoningSse),phases.Add,default,()=>phases.Add("thinking"));
Check(phases.SequenceEqual(new[]{"thinking","答案"}),"SSE reports thinking once and passes only visible text");
await Throws(()=>AiClient.ParseStream(new StringReader("data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\n"),_=>{},default),"truncated stream rejected");
await Throws(()=>AiClient.ParseStream(new StringReader("data: {\"choices\":[{\"delta\":{\"content\":\"partial\"},\"finish_reason\":\"length\"}]}\n\ndata: [DONE]\n\n"),_=>{},default),"token limit marked incomplete");
await Throws(()=>AiClient.ParseStream(new StringReader("data: "+new string('x',530000)),_=>{},default),"oversized event bounded");
await Throws(()=>AiClient.ParseStream(new StringReader("data: [DONE]\n\n"),_=>{},default),"empty response rejected");
using(var cts=new CancellationTokenSource()) { cts.Cancel(); await Throws(()=>AiClient.ParseStream(new StringReader(sse),_=>{},cts.Token),"cancellation"); }
var handler=new FakeHandler(sse,"https://example.com/v1/chat/completions");
settings.BaseUrl="https://example.com/v1"; settings.Model="deepseek-flash";
var testPrompt=new PromptAction { Prompt="Translate {{text}}" };
result.Clear(); await AiClient.Stream(settings,testPrompt,"你好",x=>result.Append(x),default,handler);
Check(result.ToString()=="你好 world"&&handler.Valid&&handler.ThinkingMode==null &&
    handler.Messages.SequenceEqual(new[]{new ChatMessage("user","Translate 你好")}),
    "compatible endpoint streams without DeepSeek-only fields");
var historyHandler=new FakeHandler(sse,"https://example.com/v1/chat/completions");
var history=new[]{new ChatMessage("user","初问"),new ChatMessage("assistant","初答"),new ChatMessage("user","请修正")};
result.Clear(); await AiClient.Stream(settings,history,x=>result.Append(x),default,historyHandler);
Check(result.ToString()=="你好 world" && historyHandler.Valid &&
    historyHandler.Messages.SequenceEqual(history),"multi-message request preserves conversation roles and order");
settings.BaseUrl="https://api.deepseek.com/v1";
foreach(var enabled in new[]{false,true}) {
    settings.DeepSeekFlashThinking=enabled;
    var officialHandler=new FakeHandler(sse,"https://api.deepseek.com/v1/chat/completions");
    await AiClient.Stream(settings,testPrompt,"你好",_=>{},default,officialHandler);
    Check(officialHandler.Valid && officialHandler.ThinkingMode==(enabled?"enabled":"disabled"),$"official DeepSeek Flash thinking toggle ({enabled})");
}
passed+=await SmartChecks.Run();
passed+=UiChecks.Run();
Console.WriteLine($"{passed} checks passed.");

sealed class FakeHandler(string sse,string endpoint):HttpMessageHandler
{
    public bool Valid;
    public string? ThinkingMode;
    public List<ChatMessage> Messages { get; } = new();
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken token)
    {
        using var body=JsonDocument.Parse(await r.Content!.ReadAsStringAsync(token));
        Messages.Clear();
        foreach(var message in body.RootElement.GetProperty("messages").EnumerateArray())
            Messages.Add(new ChatMessage(message.GetProperty("role").GetString()!,message.GetProperty("content").GetString()!));
        Valid=r.RequestUri!.AbsoluteUri==endpoint && r.Headers.Authorization?.Parameter=="test-key-not-real" &&
            Messages.Count>0 && body.RootElement.GetProperty("stream").GetBoolean();
        if(body.RootElement.TryGetProperty("thinking",out var thinking)) ThinkingMode=thinking.GetProperty("type").GetString();
        return new(HttpStatusCode.OK) { Content=new StringContent(sse,Encoding.UTF8,"text/event-stream") };
    }
}
