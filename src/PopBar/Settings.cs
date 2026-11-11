using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace PopBar;

public enum AppTheme { System, Light, Dark }
public enum ToolbarPlacement { Above, Below }
public enum PromptIconKind { Default, Emoji, NerdFont }
public sealed class PromptAction:System.ComponentModel.INotifyPropertyChanged
{
    string name="新操作";
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public string Name {
        get=>name;
        set {
            if(name==value) return;
            name=value;
            PropertyChanged?.Invoke(this,new System.ComponentModel.PropertyChangedEventArgs(nameof(Name)));
        }
    }
    public string Prompt { get; set; } = "请处理以下文字：\n{{text}}";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PromptIconKind IconKind { get; set; }
    public string Icon { get; set; } = "";
    public string IconFont { get; set; } = "";
    internal PromptAction Copy() => new() { Name=Name,Prompt=Prompt,IconKind=IconKind,Icon=Icon,IconFont=IconFont };
}
public sealed class Settings
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AppTheme Theme { get; set; } = AppTheme.System;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ToolbarPlacement ToolbarPosition { get; set; } = ToolbarPlacement.Above;
    public bool Enabled { get; set; } = true;
    public bool TerminalShift { get; set; } = true;
    public bool AutoCloseResultOnDeactivate { get; set; } = true;
    public string ResultFontFamily { get; set; } = "";
    public int ResultFontSize { get; set; } = ResultTypography.DefaultSize;
    public string ExcludedApps { get; set; } = "KeePass, KeePassXC, 1Password, Bitwarden";
    public string SearchUrl { get; set; } = "https://www.google.com/search?q={text}";
    public string BaseUrl { get; set; } = "https://api.deepseek.com/v1";
    public string Model { get; set; } = "";
    public bool DeepSeekFlashThinking { get; set; }
    public string EncryptedKey { get; set; } = "";
    public List<PromptAction> Prompts { get; set; } = new() {
        new() { Name="翻译",Prompt="将以下文字翻译成简体中文，仅返回译文：\n{{text}}" },
        new() { Name="解释",Prompt="用中文清晰解释以下文字或代码：\n{{text}}" },
        new() { Name="润色",Prompt="润色以下文字，保留原意和原语言，仅返回润色结果：\n{{text}}" }
    };
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PopBar");
    public static string FilePath => Path.Combine(Folder,"settings.json");
    public static Settings Load()
    {
        if(!File.Exists(FilePath)) return new();
        var s=JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? throw new InvalidDataException("设置文件为空。");
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
