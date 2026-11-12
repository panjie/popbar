using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace PopBar;

internal sealed record Selection(nint Window,string Text,bool Editable,AutomationElement? Element,TextPatternRange? Range);
internal static class SelectionReader
{
    internal const int MaxText=65536;
    internal const int ProviderTimeoutMs=1500;
    // A single worker slot prevents a hung provider from creating an unbounded pool of calls.
    static readonly SemaphoreSlim Gate=new(1);
    internal static string Diagnostic { get; private set; }="";
    internal static async Task<T?> Run<T>(Func<T?> read) where T:class
    {
        if(!Gate.Wait(0)) { Diagnostic="provider busy"; return null; }
        Diagnostic="no selection";
        var task=Task.Run(()=> { try { return read(); } catch(Exception ex) { Diagnostic=ex.GetType().Name; return null; } finally { Gate.Release(); } });
        if(await Task.WhenAny(task,Task.Delay(ProviderTimeoutMs))==task) return await task;
        Diagnostic="provider timeout"; return null;
    }
    internal static Task<Selection?> Read(nint window,Native.Point p,bool focused=false) => Run(() =>
        ReadNow(window,p,focused) ?? PowerPointSelection.Read(window,p,focused) ?? MarkTextSelection.Read(window,p,focused));
    static Selection? ReadNow(nint window,Native.Point p,bool focused)
    {
        var point=new Point(p.X,p.Y);
        var hit=focused?AutomationElement.FocusedElement:AutomationElement.FromPoint(point);
        var selection=ReadElement(window,hit);
        if(selection!=null || focused || hit.Current.IsPassword) return selection;
        // WinUI/Terminal hit testing can return the host pane rather than its text provider.
        // Only use focus if it belongs to this window AND contains the mouse endpoint.
        var focus=AutomationElement.FocusedElement;
        if(focus!=null && focus.Current.BoundingRectangle.Contains(point)) return ReadElement(window,focus);
        return null;
    }
    internal static bool BelongsToWindow(nint window,AutomationElement element) => AncestorsInWindow(window,element)!=null;
    static List<AutomationElement>? AncestorsInWindow(nint window,AutomationElement hit)
    {
        if(window==0) return null;
        var chain=new List<AutomationElement>();
        var element=hit;
        // Embedded WebView2 providers may belong to a different process. Prove ownership
        // through the UIA tree and native root, never through a process-name allowlist.
        for(var i=0;i<64 && element!=null;i++,element=TreeWalker.RawViewWalker.GetParent(element)) {
            var info=element.Current;
            if(info.IsPassword) return null;
            chain.Add(element);
            var hwnd=info.NativeWindowHandle;
            if(hwnd==window) return chain;
            if(hwnd!=0 && Native.GetAncestor(hwnd,2)!=window) return null;
        }
        return null;
    }
    internal static Selection? ReadElement(nint window,AutomationElement hit)
    {
        var chain=AncestorsInWindow(window,hit);
        if(chain==null) return null;
        foreach(var element in chain) {
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
        return null;
    }
    internal static Task<Selection?> Validate(Selection original) => Run(()=> {
        if(original.Element==null || original.Range==null || !BelongsToWindow(original.Window,original.Element)) return null;
        var tp=(TextPattern)original.Element.GetCurrentPattern(TextPattern.Pattern);
        var ranges=tp.GetSelection();
        if(ranges.Length!=1 || ranges[0].GetText(MaxText+1)!=original.Text) return null;
        if(ranges[0].CompareEndpoints(TextPatternRangeEndpoint.Start,original.Range,TextPatternRangeEndpoint.Start)!=0 || ranges[0].CompareEndpoints(TextPatternRangeEndpoint.End,original.Range,TextPatternRangeEndpoint.End)!=0) return null;
        var ro=ranges[0].GetAttributeValue(TextPattern.IsReadOnlyAttribute);
        return original with { Editable=ro is bool b && !b };
    });
}
