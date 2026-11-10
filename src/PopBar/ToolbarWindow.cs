using System.Windows.Interop;

namespace PopBar;

internal sealed class ToolbarWindow:Window
{
    readonly WrapPanel buttons=new();
    internal nint Handle => new WindowInteropHelper(this).Handle;
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Native.SetWindowLongPtr(Handle,-20,Native.GetWindowLongPtr(Handle,-20)|0x08000080);
        HwndSource.FromHwnd(Handle)?.AddHook((nint h,int msg,nint w,nint l,ref bool handled)=> {
            if(msg==0x21) { handled=true; return 3; } return 0;
        });
    }
    internal ToolbarWindow()
    {
        Title="PopBar · 划词工具条"; WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize;
        ShowInTaskbar=false; ShowActivated=false; Topmost=true; SizeToContent=SizeToContent.WidthAndHeight;
        AllowsTransparency=true; Background=Brushes.Transparent; Ui.Style(this); Background=Brushes.Transparent;
        Content=new Border { Background=Ui.Background,CornerRadius=new CornerRadius(10),BorderBrush=Ui.Surface,BorderThickness=new Thickness(1),Padding=new Thickness(5),Child=buttons };
    }
    internal void Present(Selection selection,Native.Point point,Settings settings,Action configure,Action<PromptAction> ai,Action<string> notify)
    {
        buttons.Children.Clear();
        void Add(string label,Action run) {
            var button=Ui.Button(label,()=> { Hide(); try { run(); } catch(Exception ex) { notify(ex.Message); } });
            button.Focusable=false; buttons.Children.Add(button);
        }
        Add("复制",()=> { if(!Actions.Copy(selection.Text)) notify("剪贴板正忙，请重试。"); });
        if(selection.Editable) {
            Add("剪切",async ()=> {
                try { var current=await SelectionReader.Validate(selection); if(current?.Editable==true && Native.GetForegroundWindow()==selection.Window) Native.Shortcut(0x58); else notify("选区已改变，请重新选择。"); }
                catch(Exception ex) { notify(ex.Message); }
            });
            Add("粘贴",async ()=> {
                try { var current=await SelectionReader.Validate(selection); if(current?.Editable==true && Native.GetForegroundWindow()==selection.Window) Native.Shortcut(0x56); else notify("选区已改变，请重新选择。"); }
                catch(Exception ex) { notify(ex.Message); }
            });
        }
        var link=Actions.Link(selection.Text);
        if(link!=null) Add("打开链接",()=>Actions.Open(link));
        else if(selection.Text.Length<=4000) Add("搜索",()=>Actions.Open(Actions.SearchUri(settings.SearchUrl,selection.Text)));
        var path=Actions.ExistingPath(selection.Text); if(path!=null) Add("文件位置",()=>Actions.Reveal(path));
        foreach(var action in settings.Prompts.Take(12)) Add(action.Name,()=>ai(action));
        Add("⚙",configure);
        var screen=System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(point.X,point.Y)).WorkingArea;
        buttons.MaxWidth=Math.Min(850,screen.Width*0.75);
        // Position using physical pixels after WPF has measured the monitor-specific DPI.
        Show(); Native.SetWindowPos(Handle,-1,point.X,point.Y,0,0,0x11); UpdateLayout();
        var dpi=VisualTreeHelper.GetDpi(this);
        buttons.MaxWidth=Math.Max(100,Math.Min(850,(screen.Width-16)/dpi.DpiScaleX));
        UpdateLayout();
        int w=(int)Math.Ceiling(ActualWidth*dpi.DpiScaleX),h=(int)Math.Ceiling(ActualHeight*dpi.DpiScaleY);
        int x=Math.Clamp(point.X-w/2,screen.Left,Math.Max(screen.Left,screen.Right-w));
        int y=point.Y-h-14; if(y<screen.Top) y=point.Y+20;
        y=Math.Clamp(y,screen.Top,Math.Max(screen.Top,screen.Bottom-h));
        Native.SetWindowPos(Handle,-1,x,y,0,0,0x11);
    }
}
