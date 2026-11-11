using PopBar;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

var passed=0;
void Check(bool yes,string name) { if(!yes) throw new Exception(name); Console.WriteLine("PASS "+name); passed++; }
async Task Throws(Func<Task> action,string name) { try { await action(); } catch(Exception) { Check(true,name); return; } throw new Exception("Expected failure: "+name); }
Check(Actions.SearchUri("https://example.com/?q={text}","中文 &x=#").Query=="?q=%E4%B8%AD%E6%96%87%20%26x%3D%23","search query encoding");
Check(Actions.Link("example.com/a?q=中文")?.Host=="example.com","bare domain");
Check(Actions.Link("javascript:alert(1)")==null && Actions.Link("file:///C:/test.exe")==null,"unsafe schemes rejected");
Check(Actions.Link("https://user:password@example.com")==null,"URL credentials rejected");
Check(Actions.Link("some words example.com")==null,"phrase remains search");
Check(AiClient.Endpoint("http://127.0.0.1:1234/v1").AbsoluteUri=="http://127.0.0.1:1234/v1/chat/completions","local endpoint");
await Throws(()=>Task.FromResult(AiClient.Endpoint("http://example.com/v1")),"remote plaintext rejected");
await Throws(()=>Task.FromResult(AiClient.Endpoint("https://example.com/v1?key=x")),"query credentials rejected");
Check(AiClient.BuildPrompt("Translate {{text}}","{{text}} $x `test`")=="Translate {{text}} $x `test`","prompt substitution is literal");
Check(AiClient.BuildPrompt("Explain","hello")=="Explain\n\nhello","prompt append");
var settings=new Settings(); settings.SetKey("test-key-not-real");
Check(settings.GetKey()=="test-key-not-real"&&!settings.EncryptedKey.Contains("test-key-not-real"),"DPAPI round trip");
Check(settings.Excludes("KeePassXC")&&!settings.Excludes("notepad"),"process exclusions");
Check(JsonSerializer.Deserialize<Settings>("{}")!.ToolbarPosition==ToolbarPlacement.Above,"legacy toolbar stays above");
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
await Throws(()=>AiClient.ParseStream(new StringReader("data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\n"),_=>{},default),"truncated stream rejected");
await Throws(()=>AiClient.ParseStream(new StringReader("data: {\"choices\":[{\"delta\":{\"content\":\"partial\"},\"finish_reason\":\"length\"}]}\n\ndata: [DONE]\n\n"),_=>{},default),"token limit marked incomplete");
await Throws(()=>AiClient.ParseStream(new StringReader("data: "+new string('x',530000)),_=>{},default),"oversized event bounded");
await Throws(()=>AiClient.ParseStream(new StringReader("data: [DONE]\n\n"),_=>{},default),"empty response rejected");
using(var cts=new CancellationTokenSource()) { cts.Cancel(); await Throws(()=>AiClient.ParseStream(new StringReader(sse),_=>{},cts.Token),"cancellation"); }
var handler=new FakeHandler(sse); settings.BaseUrl="https://example.com/v1"; settings.Model="test-model";
result.Clear(); await AiClient.Stream(settings,new PromptAction { Prompt="Translate {{text}}" },"你好",x=>result.Append(x),default,handler);
Check(result.ToString()=="你好 world"&&handler.Valid,"HTTP request and streaming integration");
passed+=UiChecks.Run();
Console.WriteLine($"{passed} checks passed.");

sealed class FakeHandler(string sse):HttpMessageHandler
{
    public bool Valid;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken token)
    {
        using var body=JsonDocument.Parse(await r.Content!.ReadAsStringAsync(token));
        Valid=r.RequestUri!.AbsoluteUri=="https://example.com/v1/chat/completions" && r.Headers.Authorization?.Parameter=="test-key-not-real" && body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()=="Translate 你好" && body.RootElement.GetProperty("stream").GetBoolean();
        return new(HttpStatusCode.OK) { Content=new StringContent(sse,Encoding.UTF8,"text/event-stream") };
    }
}
