using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace PopBar;

internal sealed class InputMonitor:IDisposable
{
    readonly Native.HookProc mouseProc,keyProc;
    readonly nint mouseHook,keyHook;
    readonly HwndSource messages;
    Native.Point start,lastClick;
    nint startWindow;
    long lastUp;
    bool pressed,startShift;
    internal event Action<nint,Native.Point,bool,bool>? Selected;
    internal event Action? Dismiss;
    internal event Action? Hotkey;
    internal bool HotkeyRegistered { get; }
    internal InputMonitor()
    {
        messages=new HwndSource(new HwndSourceParameters("PopBar messages") { ParentWindow=-3,Width=0,Height=0 });
        messages.AddHook((nint h,int msg,nint w,nint l,ref bool handled)=> { if(msg==0x312) { Hotkey?.Invoke(); handled=true; } return 0; });
        HotkeyRegistered=Native.RegisterHotKey(messages.Handle,1,0x4003,0x20);
        mouseProc=Mouse; keyProc=Key;
        mouseHook=Native.SetWindowsHookEx(14,mouseProc,Native.GetModuleHandle(null),0);
        keyHook=Native.SetWindowsHookEx(13,keyProc,Native.GetModuleHandle(null),0);
        if(mouseHook==0||keyHook==0) { Dispose(); throw new InvalidOperationException(T("InputHookFailed")); }
    }
    nint Mouse(int code,nint w,nint l)
    {
        if(code>=0) {
            var data=Marshal.PtrToStructure<Native.MouseData>(l);
            if((data.Flags&1)==0) {
                var msg=(int)w;
                if(msg==0x201) {
                    var target=Native.GetAncestor(Native.WindowFromPoint(data.Point),2);
                    pressed=!Native.OwnWindow(target); start=data.Point; startWindow=target; startShift=Native.Down(0x10);
                    if(pressed) Dismiss?.Invoke();
                } else if(msg==0x202 && pressed) {
                    pressed=false;
                    bool drag=Math.Abs(data.Point.X-start.X)>=5||Math.Abs(data.Point.Y-start.Y)>=5;
                    bool multi=Environment.TickCount64-lastUp<=Native.GetDoubleClickTime() && Math.Abs(data.Point.X-lastClick.X)<5 && Math.Abs(data.Point.Y-lastClick.Y)<5;
                    lastUp=Environment.TickCount64; lastClick=data.Point;
                    if((drag||multi||startShift) && !Native.Down(0x5B) && !Native.Down(0x5C)) Selected?.Invoke(startWindow,data.Point,startShift||Native.Down(0x10),false);
                } else if(msg is 0x204 or 0x207 or 0x20A or 0x20E) {
                    if(!Native.OwnWindow(Native.GetAncestor(Native.WindowFromPoint(data.Point),2))) Dismiss?.Invoke();
                }
            }
        }
        return Native.CallNextHookEx(mouseHook,code,w,l);
    }
    nint Key(int code,nint w,nint l)
    {
        if(code>=0 && ((int)w==0x100||(int)w==0x104)) {
            var data=Marshal.PtrToStructure<Native.KeyData>(l);
            if((data.Flags&0x10)==0 && data.Key is not (0x10 or 0x11 or 0x12 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5)) Dismiss?.Invoke();
        }
        return Native.CallNextHookEx(keyHook,code,w,l);
    }
    public void Dispose() { if(mouseHook!=0) Native.UnhookWindowsHookEx(mouseHook); if(keyHook!=0) Native.UnhookWindowsHookEx(keyHook); Native.UnregisterHotKey(messages.Handle,1); messages.Dispose(); }
}
