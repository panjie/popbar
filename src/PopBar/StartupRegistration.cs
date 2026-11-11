using Microsoft.Win32;

namespace PopBar;

internal sealed class StartupRegistration(string registryPath=StartupRegistration.RunKeyPath)
{
    const string RunKeyPath=@"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName="PopBar";
    internal const string LaunchArgument="--startup";

    internal bool IsEnabled()
    {
        using var key=Registry.CurrentUser.OpenSubKey(registryPath);
        return key?.GetValue(ValueName) is string { Length: >0 };
    }

    internal void SetEnabled(bool enabled,string? executablePath)
    {
        if(!enabled) {
            using var existing=Registry.CurrentUser.OpenSubKey(registryPath,writable:true);
            existing?.DeleteValue(ValueName,throwOnMissingValue:false);
            return;
        }

        if(string.IsNullOrEmpty(executablePath) || !Path.IsPathFullyQualified(executablePath) ||
            string.Equals(Path.GetFileName(executablePath),"dotnet.exe",StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(T("StartupRequiresExecutable"));
        var command=$"\"{executablePath}\" {LaunchArgument}";
        if(command.Length>260)
            throw new InvalidOperationException(T("StartupPathTooLong"));

        using var key=Registry.CurrentUser.CreateSubKey(registryPath);
        key.SetValue(ValueName,command,RegistryValueKind.String);
    }
}
