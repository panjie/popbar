using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PopBar;

public static class AiClient
{
    public static Uri Endpoint(string baseUrl)
    {
        if(!Uri.TryCreate(baseUrl.Trim().TrimEnd('/')+"/",UriKind.Absolute,out var uri) ||
           (uri.Scheme!="https" && !(uri.Scheme=="http" && uri.IsLoopback)) ||
           uri.UserInfo.Length>0 || uri.Query.Length>0 || uri.Fragment.Length>0)
            throw new InvalidOperationException("API 地址必须为 HTTPS（本机服务可用 HTTP），不含密钥、查询参数或 #。");
        return new Uri(uri,"chat/completions");
    }
    public static string BuildPrompt(string template,string text) => template.Contains("{{text}}",StringComparison.Ordinal)?template.Replace("{{text}}",text,StringComparison.Ordinal):template+"\n\n"+text;
    public static async Task Stream(Settings settings,PromptAction action,string text,Action<string> append,CancellationToken token,HttpMessageHandler? handler=null)
    {
        if(string.IsNullOrWhiteSpace(settings.Model)) throw new InvalidOperationException("请先在设置中填写模型 ID。");
        using var client=new HttpClient(handler??new HttpClientHandler { AllowAutoRedirect=false }) { Timeout=Timeout.InfiniteTimeSpan };
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        using var request=new HttpRequestMessage(HttpMethod.Post,Endpoint(settings.BaseUrl));
        var key=settings.GetKey();
        if(key.Length>0) request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);
        request.Content=new StringContent(JsonSerializer.Serialize(new { model=settings.Model.Trim(),stream=true,messages=new[]{new { role="user",content=BuildPrompt(action.Prompt,text) }} }),Encoding.UTF8,"application/json");
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
        if(!response.IsSuccessStatusCode) throw new InvalidOperationException($"API 请求失败（HTTP {(int)response.StatusCode}）。请检查地址、模型、密钥和服务额度。");
        using var stream=await response.Content.ReadAsStreamAsync(deadline.Token);
        using var reader=new StreamReader(stream,Encoding.UTF8);
        await ParseStream(reader,append,deadline.Token);
    }
    public static async Task ParseStream(TextReader reader,Action<string> append,CancellationToken token)
    {
        // Bound both each SSE line and the full wire response before parsing JSON.
        var line=new StringBuilder(); var eventData=new StringBuilder(); int received=0,output=0;
        var buffer=new char[2048]; bool done=false; string? finish=null;
        void Event()
        {
            if(eventData.Length==0) return;
            var data=eventData.ToString().Trim(); eventData.Clear();
            if(data=="[DONE]") { done=true; return; }
            using var json=JsonDocument.Parse(data);
            if(json.RootElement.TryGetProperty("error",out _)) throw new InvalidDataException("服务返回错误，已保留部分结果。");
            if(!json.RootElement.TryGetProperty("choices",out var choices)||choices.GetArrayLength()==0) return;
            var choice=choices[0];
            if(choice.TryGetProperty("finish_reason",out var reason) && reason.ValueKind==JsonValueKind.String) finish=reason.GetString();
            if(!choice.TryGetProperty("delta",out var delta)||!delta.TryGetProperty("content",out var content)||content.ValueKind!=JsonValueKind.String) return;
            var value=content.GetString()!; output+=value.Length;
            if(output>262144) throw new InvalidDataException("结果超过 256K 字符，已停止。");
            append(value);
        }
        void Line()
        {
            var value=line.ToString().TrimEnd('\r'); line.Clear();
            if(value.Length==0) { Event(); return; }
            if(value.StartsWith("data:",StringComparison.Ordinal)) {
                if(eventData.Length>0) eventData.Append('\n');
                eventData.Append(value.AsSpan(5).TrimStart());
                if(eventData.Length>524288) throw new InvalidDataException("响应事件过大。");
            }
        }
        while(!done)
        {
            var count=await reader.ReadAsync(buffer.AsMemory(),token);
            if(count==0) { if(line.Length>0) Line(); Event(); break; }
            received+=count; if(received>4*1024*1024) throw new InvalidDataException("响应过大，已停止。");
            for(int i=0;i<count && !done;i++) {
                if(buffer[i]=='\n') Line(); else line.Append(buffer[i]);
                if(line.Length>524288) throw new InvalidDataException("响应行过大。");
            }
        }
        if(finish=="length") throw new InvalidDataException("模型输出达到长度限制，结果不完整。");
        if(finish=="content_filter") throw new InvalidDataException("服务过滤了部分内容，结果不完整。");
        if(!done && finish!="stop") throw new InvalidDataException("连接提前结束，结果可能不完整。");
        if(output==0) throw new InvalidDataException("服务未返回文字，请检查模型是否支持 Chat Completions。");
    }
}
