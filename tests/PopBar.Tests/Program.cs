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
Check(MarkdownText.DemoteImages("![追踪](https://example.com/a.png)")=="[追踪](https://example.com/a.png)","markdown images become links");
Check(MarkdownText.DemoteImages("普通文字")=="普通文字","markdown without images unchanged");
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
Check(JsonSerializer.Deserialize<Settings>("{}")!.AutoCloseResultOnDeactivate,"result auto-close defaults on for existing settings");
var manualCloseSettings=JsonSerializer.Serialize(new Settings { AutoCloseResultOnDeactivate=false });
Check(!JsonSerializer.Deserialize<Settings>(manualCloseSettings)!.AutoCloseResultOnDeactivate,"disabled result auto-close survives saving and reloading");
Check(Enum.GetValues<ToolbarPlacement>().All(position=>JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(new Settings { ToolbarPosition=position }))!.ToolbarPosition==position),"toolbar placement JSON round trip");
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
Check(result.ToString()=="你好 world"&&handler.Valid&&handler.ThinkingMode==null,"compatible endpoint streams without DeepSeek-only fields");
settings.BaseUrl="https://api.deepseek.com/v1";
foreach(var enabled in new[]{false,true}) {
    settings.DeepSeekFlashThinking=enabled;
    var officialHandler=new FakeHandler(sse,"https://api.deepseek.com/v1/chat/completions");
    await AiClient.Stream(settings,testPrompt,"你好",_=>{},default,officialHandler);
    Check(officialHandler.Valid && officialHandler.ThinkingMode==(enabled?"enabled":"disabled"),$"official DeepSeek Flash thinking toggle ({enabled})");
}
passed+=UiChecks.Run();
Console.WriteLine($"{passed} checks passed.");

sealed class FakeHandler(string sse,string endpoint):HttpMessageHandler
{
    public bool Valid;
    public string? ThinkingMode;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken token)
    {
        using var body=JsonDocument.Parse(await r.Content!.ReadAsStringAsync(token));
        Valid=r.RequestUri!.AbsoluteUri==endpoint && r.Headers.Authorization?.Parameter=="test-key-not-real" && body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()=="Translate 你好" && body.RootElement.GetProperty("stream").GetBoolean();
        if(body.RootElement.TryGetProperty("thinking",out var thinking)) ThinkingMode=thinking.GetProperty("type").GetString();
        return new(HttpStatusCode.OK) { Content=new StringContent(sse,Encoding.UTF8,"text/event-stream") };
    }
}
