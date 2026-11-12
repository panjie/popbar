using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace PopBar;

internal enum SmartActionKind { Command, Calculate, Json, Currency, Unit }

internal sealed record SmartAction(SmartActionKind Kind,string Target,bool PowerShell=false)
{
    internal IReadOnlyList<MoneyAmount>? Amounts { get; init; }
    internal string? Source { get; init; }

    internal string Label => T(Kind switch {
        SmartActionKind.Calculate=>"SmartCalculate",SmartActionKind.Json=>"SmartJson",
        SmartActionKind.Currency=>"SmartCurrency",
        SmartActionKind.Unit=>"SmartUnit",_=>PowerShell?"RunPowerShell":"RunCommand"
    });
    internal string Glyph => Kind switch {
        SmartActionKind.Calculate=>"\uE8EF",SmartActionKind.Json=>"\uE943",
        SmartActionKind.Currency=>"¥",SmartActionKind.Unit=>"⇄",_=>"\uE756"
    };

    internal ProcessStartInfo StartInfo()
    {
        if(Kind!=SmartActionKind.Command) throw new InvalidOperationException();
        // A visible, persistent shell lets the user see output and interact with the command.
        if(PowerShell) {
            var start=new ProcessStartInfo(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe")) { UseShellExecute=true };
            start.ArgumentList.Add("-NoLogo"); start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-NoExit"); start.ArgumentList.Add("-EncodedCommand");
            start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(Target)));
            return start;
        }
        return new(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"cmd.exe")) {
            UseShellExecute=true,Arguments="/d /k "+Target
        };
    }
    internal void Run() => Process.Start(StartInfo());
}

internal static class SmartActions
{
    internal static IReadOnlyList<SmartAction> DetectAll(string text,Settings settings)
    {
        var result=new List<SmartAction>();
        if(string.IsNullOrWhiteSpace(text)||text.Length>65536) return result;
        // Keep the original path and link actions; never add smart buttons for those selections.
        if(Actions.Link(text)!=null || Path.IsPathFullyQualified(text.Trim().Trim('"'))) return result;
        var command=Detect(text);
        if(command!=null) { result.Add(command); return result; }
        if(SmartTransforms.IsCalculation(text)) result.Add(new(SmartActionKind.Calculate,text));
        if(SmartTransforms.IsJson(text)) result.Add(new(SmartActionKind.Json,text));
        if(UnitConversion.Parse(text).Count>0) result.Add(new(SmartActionKind.Unit,text));
        if(settings.CurrencyConversion && MoneyParser.Parse(text,settings.CurrencySource).Count>0)
            result.Add(new(SmartActionKind.Currency,text));
        return result;
    }
    static readonly HashSet<string> commands=new(StringComparer.OrdinalIgnoreCase) {
        "git","dotnet","npm","npx","node","pnpm","yarn","python","python3","py","pip","pip3",
        "docker","kubectl","winget","choco","scoop","curl","curl.exe","wget","ssh","scp",
        "ping","ipconfig","nslookup","netstat","tracert","where","whoami","systeminfo",
        "cmd","cmd.exe","powershell","powershell.exe","pwsh","pwsh.exe",
        "dir","cd","echo","type","copy","move","del","mkdir","rmdir","ren","set","cls"
    };

    internal static SmartAction? Detect(string text)
    {
        if(string.IsNullOrWhiteSpace(text)||text.Length>8192) return null;
        return Command(text.Trim());
    }

    static SmartAction? Command(string text)
    {
        bool powershell=false,explicitShell=false;
        var fence=Regex.Match(text,@"\A```(powershell|pwsh|ps1|cmd|bat|batch)\s*\r?\n([^\r\n]+)\r?\n```\z",RegexOptions.IgnoreCase);
        if(fence.Success) {
            powershell=new[]{"powershell","pwsh","ps1"}.Contains(fence.Groups[1].Value.ToLowerInvariant());
            explicitShell=true; text=fence.Groups[2].Value.Trim();
        } else if(text.Length>=2 && text[0]=='`' && text[^1]=='`') text=text[1..^1].Trim();
        if(text.Any(char.IsControl)) return null;
        var prompt=Regex.Match(text,@"\A(?:(PS)\s+[A-Z]:\\[^>]*>|[A-Z]:\\[^>]*>)\s*(.+)\z",RegexOptions.IgnoreCase);
        if(prompt.Success) { powershell=prompt.Groups[1].Success; explicitShell=true; text=prompt.Groups[2].Value; }
        var token=text.Split(' ',2)[0];
        bool psVerb=Regex.IsMatch(token,@"\A(?:Get|Set|New|Remove|Test|Write|Read|Start|Stop|Restart|Select|Where|ForEach|Invoke|Import|Export|Copy|Move|Rename|Clear|Add|Update|Install|Uninstall|Resolve|Out|Measure|ConvertTo|ConvertFrom)-[A-Za-z][A-Za-z0-9]*\z",RegexOptions.IgnoreCase);
        if(!explicitShell && !commands.Contains(token) && !psVerb) return null;
        if(text.Length==0) return null;
        return new(SmartActionKind.Command,text,powershell||(!explicitShell&&psVerb));
    }
}
