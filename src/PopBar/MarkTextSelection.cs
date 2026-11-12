using Accessibility;
using System.Runtime.InteropServices;
using System.Text;

namespace PopBar;

internal static class MarkTextSelection
{
    const uint ClientObject = 0xfffffffc; // OBJID_CLIENT
    const int Self = 0; // CHILDID_SELF
    const int DocumentRole = 15; // ROLE_SYSTEM_DOCUMENT
    const int ProtectedState = 0x20000000; // STATE_SYSTEM_PROTECTED
    const char EmbeddedObject = '\ufffc';
    static readonly Guid AccessibleId = typeof(IAccessible).GUID;

    [DllImport("oleacc.dll")]
    static extern int AccessibleObjectFromPoint(Native.Point point, out IAccessible? item,
        [MarshalAs(UnmanagedType.Struct)] out object child);

    [DllImport("oleacc.dll")]
    static extern int AccessibleObjectFromWindow(nint window, uint objectId, in Guid interfaceId, out IAccessible? item);

    [DllImport("oleacc.dll")]
    static extern int WindowFromAccessibleObject(IAccessible item, out nint window);

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IServiceProvider
    {
        [PreserveSig] int QueryService(in Guid service, in Guid interfaceId, out nint result);
    }

    // The gaps preserve the COM slots defined by IAccessible2 and IAccessibleText.
    [ComImport, Guid("E89F726E-C4F4-4C19-BB19-B647D7FA8478"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAccessible2
    {
        void _VtblGap1_42();
        [PreserveSig] int GetAttributes([MarshalAs(UnmanagedType.BStr)] out string attributes);
    }

    [ComImport, Guid("6B4F8BBF-F1F2-418A-B35E-A195BC4103B9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAccessibleHypertext
    {
        void _VtblGap1_4();
        [PreserveSig] int GetSelectionCount(out int count);
        void _VtblGap2_1();
        [PreserveSig] int GetSelection(int index, out int start, out int end);
        [PreserveSig] int GetText(int start, int end, [MarshalAs(UnmanagedType.BStr)] out string text);
        void _VtblGap3_12();
        [PreserveSig] int GetHyperlink(int index, [MarshalAs(UnmanagedType.IUnknown)] out object link);
        [PreserveSig] int GetHyperlinkIndex(int offset, out int index);
    }

    internal static Selection? Read(nint window, Native.Point point, bool focused)
    {
        if (Native.GetForegroundWindow() != window ||
            !string.Equals(Native.ProcessName(window), "MarkText", StringComparison.OrdinalIgnoreCase))
            return null;

        IAccessible? target;
        if (focused)
        {
            if (AccessibleObjectFromWindow(window, ClientObject, AccessibleId, out var root) != 0 || root == null)
                return null;
            target = ResolveChild(root, root.accFocus);
        }
        else
        {
            if (Native.GetAncestor(Native.WindowFromPoint(point), 2) != window ||
                AccessibleObjectFromPoint(point, out var hit, out var child) != 0 || hit == null)
                return null;
            target = ResolveChild(hit, child);
        }

        for (var depth = 0; target != null && depth < 64; depth++, target = target.accParent as IAccessible)
        {
            if (IsProtected(target, Self)) return null;
            if (target.get_accRole(Self) is not DocumentRole) continue;
            if (WindowFromAccessibleObject(target, out var owner) != 0 || Native.GetAncestor(owner, 2) != window)
                return null;

            var text = ReadDocument(target);
            // Replacement requires a UI Automation range that can be revalidated.
            return text == null ? null : new Selection(window, text, false, null, null);
        }
        return null;
    }

    static IAccessible? ResolveChild(IAccessible item, object? child)
    {
        if (child is IAccessible element) return element;
        if (child is not int id || IsProtected(item, id)) return null;
        return id == Self ? item : item.get_accChild(id) as IAccessible ?? item;
    }

    static bool IsProtected(IAccessible item, int child) =>
        item.get_accState(child) is int state && (state & ProtectedState) != 0;

    static T? GetService<T>(IAccessible item) where T : class
    {
        if (item is not IServiceProvider provider) return null;
        var iid = typeof(T).GUID;
        var status = provider.QueryService(AccessibleId, iid, out var pointer);
        if (pointer == 0) return null;
        try { return status == 0 ? Marshal.GetObjectForIUnknown(pointer) as T : null; }
        finally { Marshal.Release(pointer); }
    }

    static string? ReadDocument(IAccessible document)
    {
        var text = new StringBuilder();
        var deadline = Environment.TickCount64 + SelectionReader.ProviderTimeoutMs;
        var lineBreak = false;
        if (!AppendSelectedText(document, text, 0, deadline, ref lineBreak))
            return null;
        var result = text.ToString();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    static bool AppendSelectedText(IAccessible item, StringBuilder result, int depth, long deadline, ref bool lineBreak)
    {
        if (depth >= 64 || Environment.TickCount64 >= deadline || IsProtected(item, Self)) return false;
        var provider = GetService<IAccessibleHypertext>(item);
        if (provider == null || provider.GetSelectionCount(out var count) != 0) return false;
        if (count == 0) return true;
        if (count != 1 || provider.GetSelection(0, out var start, out var end) != 0 ||
            start < 0 || end <= start || end - start > SelectionReader.MaxText ||
            provider.GetText(start, end, out var text) != 0 || text == null || text.Length > SelectionReader.MaxText)
            return false;

        var isBlock = IsTextBlock(item);
        var initialLength = result.Length;
        lineBreak |= isBlock && initialLength > 0;

        // Chromium stores formatted spans and other blocks as embedded objects in the parent's text.
        var offset = 0;
        while (offset < text.Length)
        {
            var next = text.IndexOf(EmbeddedObject, offset);
            var limit = next < 0 ? text.Length : next;
            if (limit > offset)
            {
                if (lineBreak && result.Length > 0 && result[^1] != '\n' && text[offset] != '\n')
                    result.Append('\n');
                lineBreak = false;
                result.Append(text, offset, limit - offset);
                if (result.Length > SelectionReader.MaxText) return false;
            }
            if (next < 0) break;
            if (provider.GetHyperlinkIndex(start + next, out var index) != 0 ||
                provider.GetHyperlink(index, out var link) != 0 || link is not IAccessible child ||
                !AppendSelectedText(child, result, depth + 1, deadline, ref lineBreak))
                return false;
            offset = next + 1;
        }
        lineBreak |= isBlock && result.Length > initialLength;
        return true;
    }

    static bool IsTextBlock(IAccessible item)
    {
        var provider = GetService<IAccessible2>(item);
        if (provider == null || provider.GetAttributes(out var attributes) != 0 || attributes == null) return false;
        return attributes.Split(';').Any(attribute => attribute is
            "tag:p" or "tag:h1" or "tag:h2" or "tag:h3" or "tag:h4" or "tag:h5" or "tag:h6" or "tag:li" or "tag:pre");
    }
}
