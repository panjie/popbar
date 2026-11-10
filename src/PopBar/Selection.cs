using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace PopBar;

internal sealed record Selection(nint Window,string Text,bool Editable,AutomationElement? Element,TextPatternRange? Range);
internal static class SelectionReader
{
    internal const int MaxText=65536;
    // A single worker slot prevents a hung provider from creating an unbounded pool of calls.
    static readonly SemaphoreSlim Gate=new(1);
    internal static string Diagnostic { get; private set; }="";
    internal static async Task<T?> Run<T>(Func<T?> read) where T:class
    {
        if(!Gate.Wait(0)) { Diagnostic="provider busy"; return null; }
        Diagnostic="no selection";
        var task=Task.Run(()=> { try { return read(); } catch(Exception ex) { Diagnostic=ex.GetType().Name; return null; } finally { Gate.Release(); } });
        if(await Task.WhenAny(task,Task.Delay(1500))==task) return await task;
        Diagnostic="provider timeout"; return null;
    }
    internal static Task<Selection?> Read(nint window,Native.Point p,bool focused=false) => Run(()=>ReadNow(window,p,focused));
    static Selection? ReadNow(nint window,Native.Point p,bool focused)
    {
        var hit=focused?AutomationElement.FocusedElement:AutomationElement.FromPoint(new Point(p.X,p.Y));
        return ReadElement(window,hit);
    }
    internal static Selection? ReadElement(nint window,AutomationElement hit)
    {
        Native.GetWindowThreadProcessId(window,out var pid);
        if(hit.Current.IsPassword) return null;
        foreach(var start in new[]{hit})
        {
            var element=start;
            for(int i=0;i<10 && element!=null;i++,element=TreeWalker.ControlViewWalker.GetParent(element))
            {
                if(element.Current.ProcessId!=pid || element.Current.IsPassword) break;
                var hwnd=element.Current.NativeWindowHandle;
                if(hwnd!=0 && Native.GetAncestor(hwnd,2)!=window) break;
                if(!element.TryGetCurrentPattern(TextPattern.Pattern,out var pattern)) continue;
                var tp=(TextPattern)pattern;
                var ranges=tp.GetSelection();
                if(ranges.Length!=1) continue;
                var text=ranges[0].GetText(MaxText+1);
                if(string.IsNullOrWhiteSpace(text)||text.Length>MaxText) continue;
                var readOnly=ranges[0].GetAttributeValue(TextPattern.IsReadOnlyAttribute);
                bool editable=readOnly is bool b && !b;
                return new(window,text,editable,element,ranges[0].Clone());
            }
        }
        return null;
    }
    internal static Task<Selection?> Validate(Selection original) => Run(()=> {
        if(original.Element==null || original.Range==null || original.Element.Current.IsPassword) return null;
        var tp=(TextPattern)original.Element.GetCurrentPattern(TextPattern.Pattern);
        var ranges=tp.GetSelection();
        if(ranges.Length!=1 || ranges[0].GetText(MaxText+1)!=original.Text) return null;
        if(ranges[0].CompareEndpoints(TextPatternRangeEndpoint.Start,original.Range,TextPatternRangeEndpoint.Start)!=0 || ranges[0].CompareEndpoints(TextPatternRangeEndpoint.End,original.Range,TextPatternRangeEndpoint.End)!=0) return null;
        var ro=ranges[0].GetAttributeValue(TextPattern.IsReadOnlyAttribute);
        return original with { Editable=ro is bool b && !b };
    });
}
