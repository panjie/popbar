using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PopBar;

internal sealed record ChatMessage(string Role,string Content);

public static class AiClient
{
    public static Uri Endpoint(string baseUrl)
    {
        return new Uri(ValidateEndpoint(baseUrl.Trim().TrimEnd('/')+"/",allowQuery:false),"chat/completions");
    }
    internal static Uri ValidateEndpoint(string endpoint,bool allowQuery=true)
    {
        if(!Uri.TryCreate(endpoint.Trim(),UriKind.Absolute,out var uri) ||
           (uri.Scheme!="https" && !(uri.Scheme=="http" && uri.IsLoopback)) ||
           uri.UserInfo.Length>0 || (!allowQuery && uri.Query.Length>0) || uri.Fragment.Length>0)
            throw new InvalidOperationException(T("EndpointInvalid"));
        return uri;
    }
    internal static Uri ChatEndpoint(Settings settings) =>
        settings.OpenAiEndpoint is { } endpoint?ValidateEndpoint(endpoint):Endpoint(settings.BaseUrl);
    public static string BuildPrompt(string template,string text) => template.Contains("{{text}}",StringComparison.Ordinal)?template.Replace("{{text}}",text,StringComparison.Ordinal):template+"\n\n"+text;
    internal static bool IsOfficialDeepSeekFlash(string baseUrl,string model) =>
        (model.Trim() is "deepseek-flash" or "deepseek-v4-flash" or "deepseek-v4-flash-vision-exp") &&
        Uri.TryCreate(baseUrl.Trim(),UriKind.Absolute,out var uri) && uri.Scheme==Uri.UriSchemeHttps &&
        uri.IsDefaultPort && uri.Host=="api.deepseek.com" && uri.UserInfo.Length==0 &&
        uri.Query.Length==0 && uri.Fragment.Length==0 &&
        (uri.AbsolutePath.TrimEnd('/') is "" or "/v1" or "/chat/completions" or "/v1/chat/completions");

    public static Task Stream(Settings settings,PromptAction action,string text,Action<string> append,CancellationToken token,HttpMessageHandler? handler=null,Action? onThinking=null) =>
        Stream(settings,new[]{new ChatMessage("user",BuildPrompt(action.Prompt,text))},append,token,handler,onThinking);

    internal static async Task Stream(Settings settings,IReadOnlyList<ChatMessage> messages,Action<string> append,CancellationToken token,HttpMessageHandler? handler=null,Action? onThinking=null)
    {
        if(!settings.HasAiConfiguration) throw new InvalidOperationException(T("ModelRequired"));
        using var client=new HttpClient(handler??new HttpClientHandler { AllowAutoRedirect=false }) { Timeout=Timeout.InfiniteTimeSpan };
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        var gemini=settings.AiProvider==AiProvider.Gemini;
        using var request=gemini?GeminiApi.CreateRequest(settings,messages):CreateChatRequest(settings,messages);
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
        if(!response.IsSuccessStatusCode) throw new InvalidOperationException(T("HttpRequestFailed",(int)response.StatusCode));
        using var stream=await response.Content.ReadAsStreamAsync(deadline.Token);
        using var reader=new StreamReader(stream,Encoding.UTF8);
        await ParseStream(reader,append,deadline.Token,onThinking,gemini);
    }
    static HttpRequestMessage CreateChatRequest(Settings settings,IReadOnlyList<ChatMessage> messages)
    {
        var endpoint=ChatEndpoint(settings);
        var key=settings.GetKey();
        var payload=new Dictionary<string,object> {
            ["model"]=settings.Model.Trim(),
            ["stream"]=true,
            ["messages"]=messages.Select(message=>new { role=message.Role,content=message.Content }).ToArray()
        };
        if(IsOfficialDeepSeekFlash(endpoint.AbsoluteUri,settings.Model))
            payload["thinking"]=new { type=settings.DeepSeekFlashThinking?"enabled":"disabled" };
        var request=new HttpRequestMessage(HttpMethod.Post,endpoint) {
            Content=new StringContent(JsonSerializer.Serialize(payload),Encoding.UTF8,"application/json")
        };
        if(key.Length>0) request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);
        return request;
    }
    public static Task ParseStream(TextReader reader,Action<string> append,CancellationToken token,Action? onThinking=null) =>
        ParseStream(reader,append,token,onThinking,false);

    static async Task ParseStream(TextReader reader,Action<string> append,CancellationToken token,Action? onThinking,bool gemini)
    {
        // Bound both each SSE line and the full wire response before parsing JSON.
        var line=new StringBuilder(); var eventData=new StringBuilder(); int received=0,output=0;
        var buffer=new char[2048]; bool done=false; string? finish=null;
        void Thinking()
        {
            if(onThinking==null || output>0) return;
            onThinking();
            onThinking=null;
        }
        void Append(string value)
        {
            output+=value.Length;
            if(output>262144) throw new InvalidDataException(T("ResultTooLarge"));
            append(value);
        }
        void Event()
        {
            if(eventData.Length==0) return;
            var data=eventData.ToString().Trim(); eventData.Clear();
            if(data=="[DONE]") { done=true; return; }
            using var json=JsonDocument.Parse(data);
            if(json.RootElement.TryGetProperty("error",out _)) throw new InvalidDataException(T("ServiceError"));
            if(gemini) {
                finish=GeminiApi.ReadEvent(json.RootElement,Append,Thinking)??finish;
                return;
            }
            if(!json.RootElement.TryGetProperty("choices",out var choices)||choices.GetArrayLength()==0) return;
            var choice=choices[0];
            if(choice.TryGetProperty("finish_reason",out var reason) && reason.ValueKind==JsonValueKind.String) finish=reason.GetString();
            if(!choice.TryGetProperty("delta",out var delta) || delta.ValueKind!=JsonValueKind.Object) return;
            if(onThinking!=null && output==0 && delta.TryGetProperty("reasoning_content",out var reasoning) &&
               reasoning.ValueKind==JsonValueKind.String && reasoning.GetString() is { Length: >0 }) Thinking();
            if(!delta.TryGetProperty("content",out var content) || content.ValueKind!=JsonValueKind.String ||
               content.GetString() is not { Length: >0 } value) return;
            Append(value);
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
        if((gemini || !done) && finish!="stop") throw new InvalidDataException(T("ResponseInterrupted"));
        if(output==0) throw new InvalidDataException(T("NoResponseText"));
    }
}
