using System.Windows.Threading;
using System.Windows.Automation;

namespace PopBar;

public partial class App:Application
{
    Mutex? instance;
    InputMonitor? input;
    System.Windows.Forms.NotifyIcon? tray;
    ToolbarWindow toolbar=null!;
    Settings settings=new();
    SettingsWindow? settingsWindow;
    ResultWindow? resultWindow;
    DispatcherTimer? timer;
    int generation;
    nint selectionWindow;
    bool probing;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance=new Mutex(true,"Local\\PopBar.Personal",out var created);
        if(!created) { MessageBox.Show("PopBar 已在运行，请在系统托盘中打开设置。","PopBar"); Shutdown(); return; }
        try {
            bool first=!File.Exists(Settings.FilePath);
            try { settings=Settings.Load(); } catch { MessageBox.Show("设置文件无法读取，将使用默认设置。原文件不会自动覆盖。","PopBar"); }
            toolbar=new(); input=new();
            tray=new System.Windows.Forms.NotifyIcon { Icon=System.Drawing.SystemIcons.Application,Text="PopBar · 划词工具条",Visible=true };
            var menu=new System.Windows.Forms.ContextMenuStrip();
            var pause=new System.Windows.Forms.ToolStripMenuItem("暂停自动划词") { Checked=!settings.Enabled };
            pause.Click+=(_,_)=> { settings.Enabled=!settings.Enabled; pause.Checked=!settings.Enabled; Dismiss(); try { settings.Save(); } catch { Notify("设置保存失败。"); } };
            menu.Items.Add("设置…",null,(_,_)=>Configure()); menu.Items.Add(pause);
            menu.Items.Add("退出",null,(_,_)=>Shutdown()); menu.Opening+=(_,_)=>pause.Checked=!settings.Enabled;
            tray.ContextMenuStrip=menu; tray.DoubleClick+=(_,_)=>Configure();
            input.Dismiss+=()=>Dispatcher.BeginInvoke(Dismiss);
            input.Selected+=(window,point,shift,manual)=>Dispatcher.BeginInvoke(()=>_=Capture(window,point,shift,manual));
            input.Hotkey+=()=> { var window=Native.GetForegroundWindow(); Native.GetCursorPos(out var point); Dispatcher.BeginInvoke(()=>_=Capture(window,point,false,true)); };
            timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(180) };
            timer.Tick+=(_,_)=> { if(toolbar.IsVisible && Native.GetForegroundWindow()!=selectionWindow) Dismiss(); }; timer.Start();
            if(!input.HotkeyRegistered) Notify("Ctrl + Alt + Space 已被占用；自动划词仍可用。");
            if(first || e.Args.Contains("--settings")) Configure();
        } catch(Exception ex) { MessageBox.Show(ex.Message,"PopBar 启动失败"); Shutdown(1); }
    }
    void Dismiss() { generation++; toolbar?.Hide(); }
    static bool Terminal(string name) => new[]{"WindowsTerminal","OpenConsole","cmd","powershell","pwsh","mintty","wezterm-gui","alacritty"}.Contains(name,StringComparer.OrdinalIgnoreCase);
    async Task Capture(nint window,Native.Point point,bool shift,bool manual)
    {
        if(probing || window==0 || Native.OwnWindow(window)) return;
        var process=Native.ProcessName(window);
        if(settings.Excludes(process)||(!manual&&!settings.Enabled)||(!manual&&settings.TerminalShift&&Terminal(process)&&!shift)) return;
        var version=++generation; probing=true;
        try {
            await Task.Delay(manual?160:100);
            if(generation!=version || Native.GetForegroundWindow()!=window) return;
            var selection=await SelectionReader.Read(window,point,manual);
            if(selection==null && manual && !Terminal(process) && generation==version && Native.GetForegroundWindow()==window) selection=await CopyFallback(window);
            if(generation!=version || Native.GetForegroundWindow()!=window) return;
            if(selection==null) { if(manual) Notify("未读取到文字。请确认已选中文字，或检查该应用的辅助功能支持。"); return; }
            if(Terminal(process)) selection=selection with { Editable=false };
            selectionWindow=window;
            toolbar.Present(selection,point,settings,Configure,prompt=> {
                if(string.IsNullOrWhiteSpace(settings.Model)) { Configure(); return; }
                resultWindow?.Close(); resultWindow=new(settings,prompt,selection); resultWindow.Show();
            },Notify);
        } catch { if(manual) Notify("无法读取选区，请重试。"); }
        finally { probing=false; }
    }
    static async Task<Selection?> CopyFallback(nint window)
    {
        // Explicit shortcut only; never synthesize Ctrl+C in terminals or password controls.
        var safe=await SelectionReader.Run(()=> {
            var focus=AutomationElement.FocusedElement;
            Native.GetWindowThreadProcessId(window,out var pid);
            return focus.Current.ProcessId==pid&&!focus.Current.IsPassword ? new object():null;
        });
        if(safe==null||Native.GetForegroundWindow()!=window) return null;
        for(int i=0;i<20 && Native.ModifiersDown;i++) await Task.Delay(25);
        if(Native.ModifiersDown||Native.GetForegroundWindow()!=window) return null;
        var old=Clipboard.GetDataObject(); var backup=new DataObject();
        if(old!=null) foreach(var format in old.GetFormats(false)) { var data=old.GetData(format,false); if(data!=null) backup.SetData(format,data); }
        var before=Native.GetClipboardSequenceNumber(); uint owned=before;
        try {
            Native.Shortcut(0x43);
            for(int i=0;i<20;i++) {
                await Task.Delay(25);
                owned=Native.GetClipboardSequenceNumber();
                if(owned!=before) {
                    Native.GetWindowThreadProcessId(window,out var sourcePid);
                    Native.GetWindowThreadProcessId(Native.GetClipboardOwner(),out var ownerPid);
                    if(sourcePid!=ownerPid) { owned=before; return null; }
                    if(Native.GetForegroundWindow()!=window||!Clipboard.ContainsText()) return null;
                    var text=Clipboard.GetText();
                    if(Native.GetClipboardSequenceNumber()!=owned) { owned=before; return null; }
                    return !string.IsNullOrWhiteSpace(text)&&text.Length<=SelectionReader.MaxText ? new(window,text,false,null,null):null;
                }
            }
            return null;
        } finally {
            if(owned!=before && Native.GetClipboardSequenceNumber()==owned) {
                try { if(old==null) Clipboard.Clear(); else Clipboard.SetDataObject(backup,true); } catch { /* Another application can lock the clipboard. */ }
            }
        }
    }
    void Configure()
    {
        Dismiss();
        if(settingsWindow!=null) { settingsWindow.Activate(); return; }
        settingsWindow=new(settings,next=>settings=next); settingsWindow.Closed+=(_,_)=>settingsWindow=null; settingsWindow.Show();
    }
    void Notify(string text) { if(tray!=null) tray.ShowBalloonTip(3500,"PopBar",text,System.Windows.Forms.ToolTipIcon.Info); }
    protected override void OnExit(ExitEventArgs e)
    {
        timer?.Stop(); input?.Dispose(); resultWindow?.Close(); toolbar?.Close(); tray?.Dispose(); instance?.Dispose(); base.OnExit(e);
    }
}
