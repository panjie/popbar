using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace PopBar;

public enum AppTheme { System, Light, Dark }
public enum ToolbarPlacement { Above, Below }
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
public sealed class Settings
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public UiLanguage Language { get; set; } = UiLanguage.System;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AppTheme Theme { get; set; } = AppTheme.System;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ToolbarPlacement ToolbarPosition { get; set; } = ToolbarPlacement.Above;
    public int ToolbarIdleOpacity { get; set; } = 35;
    public bool Enabled { get; set; } = true;
    public bool TerminalShift { get; set; } = true;
    public bool CurrencyConversion { get; set; } = true;
    public bool AiSmartAnalysis { get; set; } = true;
    public string CurrencyTarget { get; set; } = "CNY";
    public string CurrencySource { get; set; } = "CNY";
    public bool AutoCloseResultOnDeactivate { get; set; } = true;
    public string ResultFontFamily { get; set; } = "";
    public int ResultFontSize { get; set; } = ResultTypography.DefaultSize;
    public string ExcludedApps { get; set; } = "KeePass, KeePassXC, 1Password, Bitwarden";
    public string SearchUrl { get; set; } = "https://www.google.com/search?q={text}";
    public string BaseUrl { get; set; } = "https://api.deepseek.com/v1";
    public string Model { get; set; } = "";
    public bool DeepSeekFlashThinking { get; set; }
    public string EncryptedKey { get; set; } = "";
    public List<PromptAction> Prompts { get; set; } = BuiltInPrompts.Create();
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PopBar");
    public static string FilePath => Path.Combine(Folder,"settings.json");
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
    public string GetKey() => string.IsNullOrEmpty(EncryptedKey)?"":Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(EncryptedKey),null,DataProtectionScope.CurrentUser));
    public void SetKey(string key) => EncryptedKey=key.Length==0?"":Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key),null,DataProtectionScope.CurrentUser));
    public bool Excludes(string name) => ExcludedApps.Split(new[]{',',';','\n'},StringSplitOptions.RemoveEmptyEntries).Any(x=>string.Equals(Path.GetFileNameWithoutExtension(x.Trim()),name,StringComparison.OrdinalIgnoreCase));
}
