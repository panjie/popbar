using System.Runtime.InteropServices;
using System.Text;

namespace PopBar;

internal static class PowerPointSelection
{
    const uint NativeObjectModel = 0xfffffff0;
    const int TextSelection = 3; // ppSelectionText
    static readonly Guid DispatchInterface = new("00020400-0000-0000-C000-000000000046");

    [StructLayout(LayoutKind.Sequential)]
    struct GuiThreadInfo
    {
        public uint Size, Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public int CaretLeft, CaretTop, CaretRight, CaretBottom;
    }

    [DllImport("user32.dll")]
    static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(nint window, StringBuilder name, int capacity);

    [DllImport("oleacc.dll")]
    static extern int AccessibleObjectFromWindow(nint window, uint objectId, in Guid interfaceId,
        [MarshalAs(UnmanagedType.IDispatch)] out object? result);

    internal static Selection? Read(nint window, Native.Point point, bool focused)
    {
        if (Native.GetForegroundWindow() != window ||
            !string.Equals(Native.ProcessName(window), "POWERPNT", StringComparison.OrdinalIgnoreCase))
            return null;

        var gui = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        if (!GetGUIThreadInfo(0, ref gui) || gui.Active != window)
            return null;

        var pane = DocumentPane(window, gui.Focus);
        if (pane == 0 || (!focused && DocumentPane(window, Native.WindowFromPoint(point)) != pane))
            return null;

        object? document = null, selection = null, range = null;
        try
        {
            if (AccessibleObjectFromWindow(pane, NativeObjectModel, DispatchInterface, out document) < 0 || document == null)
                return null;

            selection = ((dynamic)document).Selection;
            if (selection == null || (int)((dynamic)selection).Type != TextSelection)
                return null;

            range = ((dynamic)selection).TextRange;
            if (range == null || (int)((dynamic)range).Length > SelectionReader.MaxText)
                return null;

            string? text = ((dynamic)range).Text;
            if (string.IsNullOrWhiteSpace(text) || text.Length > SelectionReader.MaxText)
                return null;

            // The replacement action requires a UI Automation range to revalidate.
            return new Selection(window, text, false, null, null);
        }
        finally
        {
            if (range != null) Marshal.ReleaseComObject(range);
            if (selection != null) Marshal.ReleaseComObject(selection);
            if (document != null) Marshal.ReleaseComObject(document);
        }
    }

    static nint DocumentPane(nint window, nint child)
    {
        if (Native.GetAncestor(child, 2) != window)
            return 0;

        var name = new StringBuilder(32);
        for (var handle = child; handle != 0 && handle != window; handle = Native.GetAncestor(handle, 1))
        {
            // Office documents use paneClassDC in the original API and mdiClass in current PowerPoint.
            if (GetClassName(handle, name, name.Capacity) > 0 && name.ToString() is "paneClassDC" or "mdiClass")
                return handle;
        }
        return 0;
    }
}
