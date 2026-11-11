using System.Diagnostics;

namespace PopBar;

public static class Actions
{
    public static Uri SearchUri(string template,string text)
    {
        if(!template.Contains("{text}",StringComparison.Ordinal)) throw new InvalidOperationException(T("SearchPlaceholderRequired"));
        if(!Uri.TryCreate(template.Replace("{text}",Uri.EscapeDataString(text),StringComparison.Ordinal),UriKind.Absolute,out var u) || u.Scheme!="https" || u.UserInfo.Length>0) throw new InvalidOperationException(T("SearchHttpsRequired"));
        return u;
    }
    public static Uri? Link(string text)
    {
        text=text.Trim(); if(text.Any(char.IsWhiteSpace)||text.Length>8192) return null;
        if(!text.Contains("://",StringComparison.Ordinal)) text="https://"+text;
        if(Uri.TryCreate(text,UriKind.Absolute,out var u) && (u.Scheme=="https"||u.Scheme=="http") && u.UserInfo.Length==0 && (u.Host.Contains('.')||u.IsLoopback)) return u;
        return null;
    }
    internal static void Open(Uri url) => Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute=true });
    internal static string? ExistingPath(string text)
    {
        var path=text.Trim().Trim('"');
        return Path.IsPathFullyQualified(path)&&(File.Exists(path)||Directory.Exists(path))?path:null;
    }
    internal static void Reveal(string path)
    {
        var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe"));
        start.ArgumentList.Add("/select,"); start.ArgumentList.Add(path); Process.Start(start);
    }
    internal static bool Copy(string text)
    {
        try { Clipboard.SetText(text); return true; } catch { return false; }
    }
    internal static async Task<bool> Replace(Selection original,string result)
    {
        if(!Native.SetForegroundWindow(original.Window)) return false;
        await Task.Delay(100);
        var current=await SelectionReader.Validate(original);
        if(current?.Editable!=true || Native.GetForegroundWindow()!=original.Window || Native.ModifiersDown) return false;
        // Replacement deliberately leaves the result on the clipboard: restoring early races asynchronous paste consumers.
        if(!Copy(result)) return false;
        Native.Shortcut(0x56); return true;
    }
}
