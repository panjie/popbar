using System.Windows.Threading;
using System.Windows.Automation;

namespace PopBar;

public partial class App:Application
{
    // UI tests pump the dispatcher without installing global hooks or touching user settings.
    internal bool StartServices { get; init; } = true;
    Mutex? instance;
    InputMonitor? input;
    System.Windows.Forms.NotifyIcon? tray;
    System.Drawing.Icon? trayIcon;
    System.Windows.Forms.ToolStripItem? traySettings, trayPause, trayExit;
    ToolbarWindow toolbar=null!;
    Settings settings=new();
    SettingsWindow? settingsWindow;
    ResultWindow? resultWindow;
    SmartResultWindow? smartResultWindow;
    DispatcherTimer? timer;
    int generation;
    nint selectionWindow;
    bool probing;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if(!StartServices) return;
        var launchedAtLogin=e.Args.Contains(StartupRegistration.LaunchArgument);
        instance=new Mutex(true,"Local\\PopBar.Personal",out var created);
        if(!created) {
            if(!launchedAtLogin) {
                try { UiText.Apply(Settings.Load().Language); } catch { }
                MessageBox.Show(T("AlreadyRunning"),"PopBar");
            }
            Shutdown();
            return;
        }
        try {
            bool first=!File.Exists(Settings.FilePath);
            try { settings=Settings.Load(); } catch { MessageBox.Show(T("SettingsLoadFailed"),"PopBar"); }
            UiText.Apply(settings.Language);
            LocalizeBuiltInPromptNames();
            ThemeManager.Apply(settings.Theme);
            Microsoft.Win32.SystemEvents.UserPreferenceChanged+=SystemPreferenceChanged;
            toolbar=new(); input=new();
            using(var iconStream=Application.GetResourceStream(new Uri("pack://application:,,,/PopBar;component/Assets/PopBar.ico")).Stream)
            using(var icon=new System.Drawing.Icon(iconStream,32,32)) trayIcon=(System.Drawing.Icon)icon.Clone();
            tray=new System.Windows.Forms.NotifyIcon { Icon=trayIcon,Text=T("ToolbarTitle"),Visible=true };
            var menu=new System.Windows.Forms.ContextMenuStrip();
            var pause=new System.Windows.Forms.ToolStripMenuItem(T("PauseSelection")) { Checked=!settings.Enabled };
            pause.Click+=(_,_)=> { settings.Enabled=!settings.Enabled; pause.Checked=!settings.Enabled; Dismiss(); try { settings.Save(); } catch { Notify(T("SettingsSaveFailed")); } };
            traySettings=menu.Items.Add(T("TraySettings"),null,(_,_)=>Configure());
            trayPause=pause; menu.Items.Add(pause);
            trayExit=menu.Items.Add(T("Exit"),null,(_,_)=>Shutdown());
            menu.Opening+=(_,_)=> { ApplyLanguage(); pause.Checked=!settings.Enabled; };
            tray.ContextMenuStrip=menu; tray.DoubleClick+=(_,_)=>Configure();
            input.Dismiss+=()=>Dispatcher.BeginInvoke(Dismiss);
            input.Selected+=(window,point,shift,manual)=>Dispatcher.BeginInvoke(()=>_=Capture(window,point,shift,manual));
            input.Hotkey+=()=> { var window=Native.GetForegroundWindow(); Native.GetCursorPos(out var point); Dispatcher.BeginInvoke(()=>_=Capture(window,point,false,true)); };
            timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(180) };
            timer.Tick+=(_,_)=> { if(toolbar.IsVisible && Native.GetForegroundWindow()!=selectionWindow) Dismiss(); }; timer.Start();
            if(!input.HotkeyRegistered) Notify(T("HotkeyUnavailable"));
            if((first && !launchedAtLogin) || e.Args.Contains("--settings")) Configure();
        } catch(Exception ex) { MessageBox.Show(ex.Message,T("AppStartupFailed")); Shutdown(1); }
    }
    void Dismiss() { generation++; toolbar?.Hide(); }
    static bool Terminal(string name) => new[]{"WindowsTerminal","OpenConsole","cmd","powershell","pwsh","mintty","wezterm-gui","alacritty"}.Contains(name,StringComparer.OrdinalIgnoreCase);
    async Task Capture(nint window,Native.Point point,bool shift,bool manual)
    {
        if(probing || !InputMonitor.CanCaptureWindow(window)) return;
        var ownResult=System.Windows.Interop.HwndSource.FromHwnd(window)?.RootVisual as ResultWindow;
        var process=Native.ProcessName(window);
        if(settings.Excludes(process)||(!manual&&!settings.Enabled)||(!manual&&settings.TerminalShift&&Terminal(process)&&!shift)) return;
        Dismiss();
        var version=generation; probing=true;
        try {
            await Task.Delay(manual?160:100);
            if(generation!=version || Native.GetForegroundWindow()!=window) return;
            var selection=ownResult!=null ? ownResult.ReadBodySelection(point,manual) : await SelectionReader.Read(window,point,manual);
            if(selection==null && ownResult==null && !manual && generation==version && Native.GetForegroundWindow()==window) {
                // Chromium/WinUI providers can publish the selection after mouse-up.
                await Task.Delay(120);
                if(generation!=version || Native.GetForegroundWindow()!=window) return;
                selection=await SelectionReader.Read(window,point);
            }
            if(selection==null && ownResult==null && manual && !Terminal(process) && generation==version && Native.GetForegroundWindow()==window) selection=await CopyFallback(window);
            if(generation!=version || Native.GetForegroundWindow()!=window) return;
            if(selection==null) { if(manual) Notify(Terminal(process)?T("TerminalSelectionMissing"):T("SelectionMissing")); return; }
            if(Terminal(process)) selection=selection with { Editable=false };
            selectionWindow=window;
            toolbar.Present(selection,point,settings,Configure,prompt=> {
                if(string.IsNullOrWhiteSpace(settings.Model)) { Configure(); return; }
                resultWindow?.Close();
                var preview=new ResultWindow(settings,prompt,selection);
                resultWindow=preview;
                preview.Closed+=(_,_)=> { if(resultWindow==preview) resultWindow=null; };
                WindowPresentation.ShowForeground(preview);
            },Notify,action=> {
                smartResultWindow?.Close();
                var preview=new SmartResultWindow(action,selection,settings);
                smartResultWindow=preview;
                preview.Closed+=(_,_)=> { if(smartResultWindow==preview) smartResultWindow=null; };
                WindowPresentation.ShowForeground(preview);
            },placement:settings.ToolbarPositionFor(process));
        } catch { if(manual) Notify(T("SelectionReadFailed")); }
        finally { probing=false; }
    }
    static async Task<Selection?> CopyFallback(nint window)
    {
        // Explicit shortcut only; never synthesize Ctrl+C in terminals or password controls.
        var safe=await SelectionReader.Run(()=> {
            var focus=AutomationElement.FocusedElement;
            return SelectionReader.BelongsToWindow(window,focus) ? new object():null;
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
        ApplyLanguage();
        settingsWindow=new(settings,next=> {
            settings=next;
            ApplyLanguage();
            LocalizeBuiltInPromptNames();
            ThemeManager.Apply(next.Theme);
            resultWindow?.SetAutoCloseOnDeactivate(next.AutoCloseResultOnDeactivate);
            resultWindow?.ApplySettings(next);
            resultWindow?.ApplyTypography(next);
        });
        settingsWindow.Closed+=(_,_)=>settingsWindow=null;
        settingsWindow.Show();
    }
    void LocalizeBuiltInPromptNames()
    {
        foreach(var prompt in settings.Prompts) BuiltInPrompts.LocalizeName(prompt);
    }
    void ApplyLanguage()
    {
        if(!UiText.Apply(settings.Language)) return;
        Dismiss();
        LocalizeBuiltInPromptNames();
        if(tray!=null) tray.Text=T("ToolbarTitle");
        if(traySettings!=null) traySettings.Text=T("TraySettings");
        if(trayPause!=null) trayPause.Text=T("PauseSelection");
        if(trayExit!=null) trayExit.Text=T("Exit");
        toolbar?.RefreshLanguage();
        resultWindow?.RefreshLanguage();
    }
    void SystemPreferenceChanged(object? sender,Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if(settings.Theme!=AppTheme.System && settings.Language!=UiLanguage.System) return;
        Dispatcher.BeginInvoke(()=> {
            if(settings.Theme==AppTheme.System) ThemeManager.Apply(AppTheme.System);
            if(settings.Language==UiLanguage.System) ApplyLanguage();
        });
    }
    void Notify(string text) { if(tray!=null) tray.ShowBalloonTip(3500,"PopBar",text,System.Windows.Forms.ToolTipIcon.Info); }
    protected override void OnExit(ExitEventArgs e)
    {
        Microsoft.Win32.SystemEvents.UserPreferenceChanged-=SystemPreferenceChanged;
        timer?.Stop(); input?.Dispose(); resultWindow?.Close(); smartResultWindow?.Close(); toolbar?.Close(); tray?.Dispose(); trayIcon?.Dispose(); instance?.Dispose(); base.OnExit(e);
    }
}
