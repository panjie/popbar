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
            throw new InvalidOperationException(T("EndpointInvalid"));
        return new Uri(uri,"chat/completions");
    }
    public static string BuildPrompt(string template,string text) => template.Contains("{{text}}",StringComparison.Ordinal)?template.Replace("{{text}}",text,StringComparison.Ordinal):template+"\n\n"+text;
    internal static bool IsOfficialDeepSeekFlash(string baseUrl,string model) =>
        (model.Trim() is "deepseek-flash" or "deepseek-v4-flash" or "deepseek-v4-flash-vision-exp") &&
        Uri.TryCreate(baseUrl.Trim(),UriKind.Absolute,out var uri) && uri.Scheme==Uri.UriSchemeHttps &&
        uri.IsDefaultPort && uri.Host=="api.deepseek.com" && uri.UserInfo.Length==0 &&
        uri.Query.Length==0 && uri.Fragment.Length==0 && (uri.AbsolutePath.TrimEnd('/') is "" or "/v1");

    public static async Task Stream(Settings settings,PromptAction action,string text,Action<string> append,CancellationToken token,HttpMessageHandler? handler=null,Action? onThinking=null)
    {
        if(string.IsNullOrWhiteSpace(settings.Model)) throw new InvalidOperationException(T("ModelRequired"));
        using var client=new HttpClient(handler??new HttpClientHandler { AllowAutoRedirect=false }) { Timeout=Timeout.InfiniteTimeSpan };
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        using var request=new HttpRequestMessage(HttpMethod.Post,Endpoint(settings.BaseUrl));
        var key=settings.GetKey();
        if(key.Length>0) request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);
        var payload=new Dictionary<string,object> {
            ["model"]=settings.Model.Trim(),
            ["stream"]=true,
            ["messages"]=new[]{new { role="user",content=BuildPrompt(action.Prompt,text) }}
        };
        if(IsOfficialDeepSeekFlash(settings.BaseUrl,settings.Model))
            payload["thinking"]=new { type=settings.DeepSeekFlashThinking?"enabled":"disabled" };
        request.Content=new StringContent(JsonSerializer.Serialize(payload),Encoding.UTF8,"application/json");
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
        if(!response.IsSuccessStatusCode) throw new InvalidOperationException(T("HttpRequestFailed",(int)response.StatusCode));
        using var stream=await response.Content.ReadAsStreamAsync(deadline.Token);
        using var reader=new StreamReader(stream,Encoding.UTF8);
        await ParseStream(reader,append,deadline.Token,onThinking);
    }
    public static async Task ParseStream(TextReader reader,Action<string> append,CancellationToken token,Action? onThinking=null)
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
            if(json.RootElement.TryGetProperty("error",out _)) throw new InvalidDataException(T("ServiceError"));
            if(!json.RootElement.TryGetProperty("choices",out var choices)||choices.GetArrayLength()==0) return;
            var choice=choices[0];
            if(choice.TryGetProperty("finish_reason",out var reason) && reason.ValueKind==JsonValueKind.String) finish=reason.GetString();
            if(!choice.TryGetProperty("delta",out var delta) || delta.ValueKind!=JsonValueKind.Object) return;
            if(onThinking!=null && output==0 && delta.TryGetProperty("reasoning_content",out var reasoning) &&
               reasoning.ValueKind==JsonValueKind.String && reasoning.GetString() is { Length: >0 }) {
                onThinking();
                onThinking=null;
            }
            if(!delta.TryGetProperty("content",out var content) || content.ValueKind!=JsonValueKind.String ||
               content.GetString() is not { Length: >0 } value) return;
            output+=value.Length;
            if(output>262144) throw new InvalidDataException(T("ResultTooLarge"));
            append(value);
        }
        void Line()
        {
            var value=line.ToString().TrimEnd('\r'); line.Clear();
            if(value.Length==0) { Event(); return; }
            if(value.StartsWith("data:",StringComparison.Ordinal)) {
                if(eventData.Length>0) eventData.Append('\n');
                eventData.Append(value.AsSpan(5).TrimStart());
                if(eventData.Length>524288) throw new InvalidDataException(T("ResponseEventTooLarge"));
            }
        }
        while(!done)
        {
            var count=await reader.ReadAsync(buffer.AsMemory(),token);
            if(count==0) { if(line.Length>0) Line(); Event(); break; }
            received+=count; if(received>4*1024*1024) throw new InvalidDataException(T("ResponseTooLarge"));
            for(int i=0;i<count && !done;i++) {
                if(buffer[i]=='\n') Line(); else line.Append(buffer[i]);
                if(line.Length>524288) throw new InvalidDataException(T("ResponseLineTooLarge"));
            }
        }
        if(finish=="length") throw new InvalidDataException(T("ModelOutputLimit"));
        if(finish=="content_filter") throw new InvalidDataException(T("ModelContentFilter"));
        if(!done && finish!="stop") throw new InvalidDataException(T("ResponseInterrupted"));
        if(output==0) throw new InvalidDataException(T("NoResponseText"));
    }
}
