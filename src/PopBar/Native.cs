using System.Runtime.InteropServices;
using System.ComponentModel;

namespace PopBar;

internal static class Native
{
    internal delegate nint HookProc(int code, nint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; public Point(int x,int y) { X=x; Y=y; } }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseData { public Point Point; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct KeyData { public uint Key, Scan, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct Input { public uint Type; public InputUnion U; }
    [StructLayout(LayoutKind.Explicit)] internal struct InputUnion { [FieldOffset(0)] public KeyInput Key; [FieldOffset(0)] public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] internal struct KeyInput { public ushort Vk, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseInput { public int X,Y; public uint Data,Flags,Time; public nuint Extra; }
    [DllImport("user32.dll", SetLastError=true)] internal static extern nint SetWindowsHookEx(int id, HookProc proc, nint module, uint thread);
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] internal static extern nint CallNextHookEx(nint hook,int code,nint w,nint l);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] internal static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetAncestor(nint hwnd,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(nint hwnd,System.Text.StringBuilder name,int capacity);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint WindowFromPoint(Point p);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd,out uint pid);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] internal static extern uint GetDoubleClickTime();
    [DllImport("user32.dll", SetLastError=true)] internal static extern bool RegisterHotKey(nint hwnd,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(nint hwnd,int id);
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint h,int index);
    [DllImport("user32.dll", EntryPoint="SetWindowLongPtrW")] internal static extern nint SetWindowLongPtr(nint h,int index,nint value);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint h,nint after,int x,int y,int cx,int cy,uint flags);
    [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] internal static extern nint GetClipboardOwner();
    [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint count,Input[] inputs,int size);
    internal static bool Down(int key) => GetAsyncKeyState(key)<0;
    internal static bool OwnWindow(nint h) { GetWindowThreadProcessId(h,out var pid); return pid==Environment.ProcessId; }
    internal static bool IsDetachedChromiumRenderer(nint hwnd,nint window)
    {
        if(IsWindowVisible(hwnd)) return false;
        GetWindowThreadProcessId(hwnd,out var rendererPid);
        GetWindowThreadProcessId(window,out var windowPid);
        if(rendererPid==0 || rendererPid!=windowPid) return false;
        var name=new System.Text.StringBuilder(64);
        return GetClassName(hwnd,name,name.Capacity)>0 && name.ToString()=="Chrome_RenderWidgetHostHWND";
    }
    internal static string ProcessName(nint h) { try { GetWindowThreadProcessId(h,out var p); return System.Diagnostics.Process.GetProcessById((int)p).ProcessName; } catch { return ""; } }
    internal static bool ModifiersDown => Down(0x10)||Down(0x11)||Down(0x12)||Down(0x5B)||Down(0x5C);
    internal static void Shortcut(ushort key)
    {
        if (ModifiersDown) throw new InvalidOperationException(T("ReleaseModifiers"));
        Input Make(ushort k,bool up) => new() { Type=1,U=new() { Key=new() { Vk=k, Flags=up?2u:0u } } };
        var inputs=new[] { Make(0x11,false),Make(key,false),Make(key,true),Make(0x11,true) };
        if(SendInput(4,inputs,Marshal.SizeOf<Input>())!=4) throw new Win32Exception(T("SendShortcutFailed"));
    }
}
