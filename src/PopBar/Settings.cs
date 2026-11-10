using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace PopBar;

public sealed class PromptAction
{
    public string Name { get; set; } = "新操作";
    public string Prompt { get; set; } = "请处理以下文字：\n{{text}}";
}
public sealed class Settings
{
    public bool Enabled { get; set; } = true;
    public bool TerminalShift { get; set; } = true;
    public string ExcludedApps { get; set; } = "KeePass, KeePassXC, 1Password, Bitwarden";
    public string SearchUrl { get; set; } = "https://www.google.com/search?q={text}";
    public string BaseUrl { get; set; } = "https://api.deepseek.com/v1";
    public string Model { get; set; } = "";
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
