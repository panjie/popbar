using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Documents;

namespace PopBar;

internal static class MarkdownText
{
    static readonly Regex Images=new Regex(@"!\[([^\]]*)\]\(([^)\r\n]*)\)",RegexOptions.Compiled);
    internal static string DemoteImages(string markdown) => Images.Replace(markdown,"[$1]($2)");
    internal static string FormatUserTurn(string label,string text)
    {
        var lines=DemoteImages(text.Replace("\r\n","\n").Replace('\r','\n')).Split('\n');
        var body=string.Join("\n",lines.Select(line=>"> "+EscapeUserLine(line)));
        return $"### {label}\n\n{body}\n\n";
    }
    internal static string FormatAssistantTurn(string label,string markdown) =>
        $"### {label}\n\n{CloseCodeFences(markdown)}\n\n";
    static string EscapeUserLine(string line)
    {
        var escaped=new StringBuilder(line.Length);
        foreach(var value in line) {
            switch(value) {
                case '&': escaped.Append("&amp;"); break;
                case '<': escaped.Append("&lt;"); break;
                case '>': escaped.Append("&gt;"); break;
                case '\\' or '`' or '*' or '_' or '{' or '}' or '[' or ']' or '(' or ')' or
                    '#' or '+' or '-' or '.' or '!' or '|' or '~':
                    escaped.Append('\\').Append(value); break;
                default: escaped.Append(value); break;
            }
        }
        return escaped.ToString();
    }
    static string CloseCodeFences(string markdown)
    {
        char marker='\0'; int length=0;
        foreach(var line in markdown.Replace("\r\n","\n").Split('\n')) {
            var trimmed=line.TrimStart();
            if(line.Length-trimmed.Length>3) continue;
            var current=trimmed.Length>0 && (trimmed[0]=='`' || trimmed[0]=='~')?trimmed[0]:'\0';
            if(current=='\0') continue;
            var count=0; while(count<trimmed.Length && trimmed[count]==current) count++;
            if(count<3) continue;
            var rest=trimmed[count..].Trim();
            if(marker=='\0') { marker=current; length=count; }
            else if(current==marker && count>=length && rest.Length==0) { marker='\0'; length=0; }
        }
        return marker=='\0'?markdown:markdown+"\n"+new string(marker,length);
    }
    internal static string Plain(FlowDocument? document)
    {
        if(document is null) return "";
        return new TextRange(document.ContentStart,document.ContentEnd).Text.TrimEnd('\r','\n');
    }
}
