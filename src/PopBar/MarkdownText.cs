using System.Text.RegularExpressions;
using System.Windows.Documents;

namespace PopBar;

internal static class MarkdownText
{
    static readonly Regex Images=new Regex(@"!\[([^\]]*)\]\(([^)\r\n]*)\)",RegexOptions.Compiled);
    internal static string DemoteImages(string markdown) => Images.Replace(markdown,"[$1]($2)");
    internal static string Plain(FlowDocument? document)
    {
        if(document is null) return "";
        return new TextRange(document.ContentStart,document.ContentEnd).Text.TrimEnd('\r','\n');
    }
}
