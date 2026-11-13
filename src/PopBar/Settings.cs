using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace PopBar;

public enum AppTheme { System, Light, Dark }
public enum ToolbarPlacement { Above, Below }
// FujitsuGemini is retained for deserializing older settings.
public enum AiProvider { ChatCompletions, Gemini, FujitsuGemini }
public sealed record AppToolbarPosition(
    string[] Executables,
    [property:JsonConverter(typeof(JsonStringEnumConverter))] ToolbarPlacement Position);
// Default is retained only for reading older settings.
public enum PromptIconKind { Default, Emoji, NerdFont }
public sealed class PromptAction:System.ComponentModel.INotifyPropertyChanged,IJsonOnDeserialized
{
    string name=T("NewPromptName");
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public string Name {
        get=>name;
        set {
            if(name==value) return;
            name=value;
            PropertyChanged?.Invoke(this,new System.ComponentModel.PropertyChangedEventArgs(nameof(Name)));
        }
    }
    public string Prompt { get; set; } = T("NewPromptTemplate");
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PromptIconKind IconKind { get=>iconKind; set { iconKind=value; hasIconKind=true; } }
    PromptIconKind iconKind=PromptIconKind.Emoji;
    bool hasIconKind;
    public string Icon { get; set; } = "✨";
    public string IconFont { get; set; } = "";
    void IJsonOnDeserialized.OnDeserialized()
    {
        if(hasIconKind && IconKind!=PromptIconKind.Default) return;
        IconKind=PromptIconKind.Emoji;
        Icon=BuiltInPrompts.LegacyEmoji(Name);
        IconFont="";
    }
    internal PromptAction Copy() => new() { Name=Name,Prompt=Prompt,IconKind=IconKind,Icon=Icon,IconFont=IconFont };
}
public sealed class Settings:IJsonOnDeserializing,IJsonOnDeserialized
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public UiLanguage Language { get; set; } = UiLanguage.System;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AppTheme Theme { get; set; } = AppTheme.System;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ToolbarPlacement ToolbarPosition { get; set; } = ToolbarPlacement.Above;
    public List<AppToolbarPosition> ToolbarAppPositions { get; set; } = AppToolbarPresets.CreateDefaults();
    [JsonInclude]
    internal bool ToolbarAppPositionsInitialized { get; private set; } = true;
    public int ToolbarIdleOpacity { get; set; } = 35;
    public bool Enabled { get; set; } = true;
    public bool TerminalShift { get; set; } = true;
    public bool CurrencyConversion { get; set; } = true;
    public bool AiSmartAnalysis { get; set; } = true;
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]
    public string? AiSmartPrompt { get; set; }
    public string CurrencyTarget { get; set; } = "CNY";
    public string CurrencySource { get; set; } = "CNY";
    public bool AutoCloseResultOnDeactivate { get; set; } = true;
    public string ResultFontFamily { get; set; } = "";
    public int ResultFontSize { get; set; } = ResultTypography.DefaultSize;
    public string ExcludedApps { get; set; } = "KeePass, KeePassXC, 1Password, Bitwarden";
    public string SearchUrl { get; set; } = "https://www.google.com/search?q={text}";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AiProvider AiProvider { get; set; } = AiProvider.ChatCompletions;
    public string BaseUrl { get; set; } = "https://api.deepseek.com/v1";
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]
    public string? OpenAiEndpoint { get; set; }
    internal string EffectiveOpenAiEndpoint => OpenAiEndpoint??BaseUrl.Trim().TrimEnd('/')+"/chat/completions";
    public string Model { get; set; } = "";
    public bool DeepSeekFlashThinking { get; set; }
    public string EncryptedKey { get; set; } = "";
    public string GeminiEndpoint { get; set; } = GeminiApi.DefaultEndpoint;
    public string GeminiKeyHeader { get; set; } = GeminiApi.DefaultKeyHeader;
    public string GeminiModel { get; set; } = "";
    public string EncryptedGeminiKey { get; set; } = "";
    [JsonInclude,JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]
    internal string? FujitsuGeminiModel { get; set; }
    [JsonInclude,JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]
    internal string? EncryptedFujitsuGeminiKey { get; set; }
    internal string ActiveModel => AiProvider==AiProvider.Gemini?GeminiModel:Model;
    internal bool HasAiConfiguration => AiProvider==AiProvider.Gemini
        ? !string.IsNullOrWhiteSpace(GeminiEndpoint) && (!GeminiApi.UsesModel(GeminiEndpoint) || !string.IsNullOrWhiteSpace(GeminiModel))
        : !string.IsNullOrWhiteSpace(Model);
    public List<PromptAction> Prompts { get; set; } = BuiltInPrompts.Create();
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PopBar");
    public static string FilePath => Path.Combine(Folder,"settings.json");
    void IJsonOnDeserializing.OnDeserializing() => ToolbarAppPositionsInitialized=false;
    void IJsonOnDeserialized.OnDeserialized()
    {
        if(AiProvider==AiProvider.FujitsuGemini ||
           (AiProvider==AiProvider.ChatCompletions && GeminiEndpoint==GeminiApi.DefaultEndpoint &&
            string.IsNullOrEmpty(GeminiModel) && string.IsNullOrEmpty(EncryptedGeminiKey) &&
            !string.IsNullOrEmpty(EncryptedFujitsuGeminiKey))) {
            if(AiProvider==AiProvider.FujitsuGemini) AiProvider=AiProvider.Gemini;
            GeminiEndpoint="https://api.ai-service.global.fujitsu.com/ai-foundation/chat-ai/gemini/{model}:streamGenerateContentSse";
            GeminiModel=FujitsuGeminiModel??"gemini-3.x-flash";
            EncryptedGeminiKey=EncryptedFujitsuGeminiKey??"";
        }
        FujitsuGeminiModel=EncryptedFujitsuGeminiKey=null;
        ToolbarAppPositions??=[];
        // Older versions stored only opt-in rules. Add presets once, so later removals persist.
        if(ToolbarAppPositionsInitialized) return;
        AppToolbarPresets.AddMissing(ToolbarAppPositions);
        ToolbarAppPositionsInitialized=true;
    }
    public static Settings Load()
    {
        if(!File.Exists(FilePath)) return new();
        var s=JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? throw new InvalidDataException(T("EmptySettings"));
        s.Prompts ??= new(); return s;
    }
    public void Save()
    {
        Directory.CreateDirectory(Folder);
        var temp=FilePath+".tmp";
        File.WriteAllText(temp,JsonSerializer.Serialize(this,new JsonSerializerOptions { WriteIndented=true }));
        File.Move(temp,FilePath,true);
    }
    public string GetKey() => GetKey(AiProvider);
    public void SetKey(string key) => SetKey(AiProvider,key);
    internal string GetKey(AiProvider provider)
    {
        var encrypted=provider==AiProvider.Gemini?EncryptedGeminiKey:EncryptedKey;
        return string.IsNullOrEmpty(encrypted)?"":Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(encrypted),null,DataProtectionScope.CurrentUser));
    }
    internal void SetKey(AiProvider provider,string key)
    {
        var encrypted=key.Length==0?"":Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key),null,DataProtectionScope.CurrentUser));
        if(provider==AiProvider.Gemini) EncryptedGeminiKey=encrypted;
        else EncryptedKey=encrypted;
    }
    public bool Excludes(string name) => ExcludedApps.Split(new[]{',',';','\n'},StringSplitOptions.RemoveEmptyEntries).Any(x=>string.Equals(Path.GetFileNameWithoutExtension(x.Trim()),name,StringComparison.OrdinalIgnoreCase));
    internal ToolbarPlacement ToolbarPositionFor(string processName) => ToolbarAppPositions?
        .FirstOrDefault(rule=>rule?.Executables?.Contains(processName+".exe",StringComparer.OrdinalIgnoreCase)==true)
        ?.Position??ToolbarPosition;
}
