using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PopBar;

internal static class GeminiApi
{
    internal const string DefaultEndpoint="https://generativelanguage.googleapis.com/v1beta/models/{model}:streamGenerateContent?alt=sse";
    internal const string DefaultKeyHeader="x-goog-api-key";

    internal static bool UsesModel(string endpoint) => endpoint.Contains("{model}",StringComparison.Ordinal);

    internal static Uri Endpoint(string endpoint,string model)
    {
        if(!UsesModel(endpoint)) return AiClient.ValidateEndpoint(endpoint);
        model=model.Trim();
        if(model.StartsWith("models/",StringComparison.Ordinal)) model=model["models/".Length..];
        if(model.Length==0 || !char.IsAsciiLetterOrDigit(model[0]) ||
           model.Any(c=>!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.')))
            throw new InvalidOperationException(T("GeminiModelInvalid"));
        return AiClient.ValidateEndpoint(endpoint.Replace("{model}",model,StringComparison.Ordinal));
    }

    internal static void ValidateKeyHeader(string name)
    {
        if(string.IsNullOrWhiteSpace(name) || name.Trim().Any(c=>!char.IsAsciiLetterOrDigit(c) && c!='-'))
            throw new InvalidOperationException(T("GeminiKeyHeaderInvalid"));
    }

    internal static HttpRequestMessage CreateRequest(Settings settings,IReadOnlyList<ChatMessage> messages)
    {
        var endpoint=Endpoint(settings.GeminiEndpoint,settings.GeminiModel);
        var key=settings.GetKey().Trim();
        var payload=new Dictionary<string,object> {
            ["contents"]=messages.Where(message=>message.Role!="system").Select(message=>new {
                role=message.Role=="assistant"?"model":message.Role,
                parts=new[]{new { text=message.Content }}
            }).ToArray()
        };
        var instructions=messages.Where(message=>message.Role=="system").Select(message=>new { text=message.Content }).ToArray();
        if(instructions.Length>0) payload["systemInstruction"]=new { parts=instructions };
        var request=new HttpRequestMessage(HttpMethod.Post,endpoint) {
            Content=new StringContent(JsonSerializer.Serialize(payload),Encoding.UTF8,"application/json")
        };
        if(key.Length>0) {
            var header=settings.GeminiKeyHeader.Trim();
            ValidateKeyHeader(header);
            if(header.Equals("Authorization",StringComparison.OrdinalIgnoreCase))
                request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);
            else request.Headers.Add(header,key);
        }
        return request;
    }

    internal static string? ReadEvent(JsonElement response,Action<string> append,Action onThinking)
    {
        if(response.TryGetProperty("promptFeedback",out var feedback) &&
           feedback.TryGetProperty("blockReason",out var blocked) &&
           blocked.GetString() is { Length: >0 } reason && reason!="BLOCK_REASON_UNSPECIFIED")
            throw new InvalidDataException(T("ModelContentFilter"));
        if(!response.TryGetProperty("candidates",out var candidates) || candidates.GetArrayLength()==0) return null;
        var candidate=candidates[0];
        if(candidate.TryGetProperty("content",out var content) && content.TryGetProperty("parts",out var parts)) {
            foreach(var part in parts.EnumerateArray()) {
                if(!part.TryGetProperty("text",out var text) || text.ValueKind!=JsonValueKind.String ||
                   text.GetString() is not { Length: >0 } value) continue;
                if(part.TryGetProperty("thought",out var thought) && thought.ValueKind==JsonValueKind.True) onThinking();
                else append(value);
            }
        }
        return (candidate.TryGetProperty("finishReason",out var finish)?finish.GetString():null) switch {
            null or "" or "FINISH_REASON_UNSPECIFIED"=>null,
            "STOP"=>"stop",
            "MAX_TOKENS" or "CONTINUATION"=>"length",
            "SAFETY" or "RECITATION" or "BLOCKLIST" or "PROHIBITED_CONTENT" or "SPII" or "ESCALATION"=>"content_filter",
            _=>throw new InvalidDataException(T("ServiceError"))
        };
    }
}
