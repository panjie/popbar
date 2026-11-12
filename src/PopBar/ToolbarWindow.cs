using System.Windows.Interop;
using System.Windows.Media.Animation;

namespace PopBar;

internal sealed class ToolbarWindow:Window
{
    internal const double IdleOpacity=0.35;
    readonly Border surface;
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
        RefreshLanguage(); WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize;
        ShowInTaskbar=false; ShowActivated=false; Topmost=true; SizeToContent=SizeToContent.WidthAndHeight;
        AllowsTransparency=true; Background=Brushes.Transparent; Ui.Style(this); Background=Brushes.Transparent;
        surface=new Border { CornerRadius=new CornerRadius(8),BorderThickness=new Thickness(1),Padding=new Thickness(3),Margin=new Thickness(6),Child=buttons,Effect=new System.Windows.Media.Effects.DropShadowEffect { BlurRadius=12,ShadowDepth=2,Opacity=0.25,Direction=270,Color=Colors.Black } };
        surface.Name="ToolbarSurface"; surface.Opacity=IdleOpacity;
        surface.MouseEnter+=(_,_)=>Fade(1);
        surface.MouseLeave+=(_,_)=>Fade(IdleOpacity);
        surface.SetResourceReference(Border.BackgroundProperty,"SurfaceBrush");
        surface.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");
        Content=surface;
    }
    internal void RefreshLanguage()
    {
        Title=T("ToolbarTitle");
        Ui.ApplyTextLanguage(this);
    }
    void Fade(double target)
    {
        surface.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(target,TimeSpan.FromMilliseconds(120)) { EasingFunction=new QuadraticEase { EasingMode=EasingMode.EaseOut } });
    }
    internal void Present(Selection selection,Native.Point point,Settings settings,Action configure,Action<PromptAction> ai,Action<string> notify)
    {
        surface.BeginAnimation(UIElement.OpacityProperty,null); surface.Opacity=IdleOpacity;
        buttons.Children.Clear();
        void Separator() {
            if(buttons.Children.Count==0 || buttons.Children[^1] is Border) return;
            var separator=new Border { Width=1,Height=16,Margin=new Thickness(3,0,3,0),VerticalAlignment=VerticalAlignment.Center };
            separator.SetResourceReference(Border.BackgroundProperty,"BorderBrush");
            buttons.Children.Add(separator);
        }
        void Add(string glyph,string label,Action run,PromptAction? prompt=null) {
            var button=Ui.GlyphButton(glyph,label,()=> { Hide(); try { run(); } catch(Exception ex) { notify(ex.Message); } });
            if(prompt!=null) button.Content=new Viewbox { Width=18,Height=18,Stretch=Stretch.Uniform,Child=PromptIcons.Create(prompt,out _) };
            button.Focusable=false; buttons.Children.Add(button);
        }
        Add("\uE8C8",T("Copy"),()=> { if(!Actions.Copy(selection.Text)) notify(T("ClipboardBusy")); });
        if(selection.Editable) {
            Add("\uE8C6",T("Cut"),async ()=> {
                try { var current=await SelectionReader.Validate(selection); if(current?.Editable==true && Native.GetForegroundWindow()==selection.Window) Native.Shortcut(0x58); else notify(T("SelectionChanged")); }
                catch(Exception ex) { notify(ex.Message); }
            });
            Add("\uE77F",T("Paste"),async ()=> {
                try { var current=await SelectionReader.Validate(selection); if(current?.Editable==true && Native.GetForegroundWindow()==selection.Window) Native.Shortcut(0x56); else notify(T("SelectionChanged")); }
                catch(Exception ex) { notify(ex.Message); }
            });
        }
        Separator();
        var link=Actions.Link(selection.Text);
        if(link!=null) Add("\uE8A7",T("OpenLink"),()=>Actions.Open(link));
        else if(selection.Text.Length<=4000) Add("\uE721",T("Search"),()=>Actions.Open(Actions.SearchUri(settings.SearchUrl,selection.Text)));
        var path=Actions.ExistingPath(selection.Text); if(path!=null) Add("\uE8B7",T("FileLocation"),()=>Actions.Reveal(path));
        Separator();
        foreach(var action in settings.Prompts.Take(12)) {
            Add("",action.Name,()=>ai(action),action);
        }
        Separator();
        Add("\uE713",T("Settings"),configure);
        var screen=System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(point.X,point.Y)).WorkingArea;
        buttons.MaxWidth=Math.Min(850,screen.Width*0.75);
        // Position using physical pixels after WPF has measured the monitor-specific DPI.
        Show(); Native.SetWindowPos(Handle,-1,point.X,point.Y,0,0,0x11); UpdateLayout();
        var dpi=VisualTreeHelper.GetDpi(this);
        buttons.MaxWidth=Math.Max(100,Math.Min(850,(screen.Width-16)/dpi.DpiScaleX));
        UpdateLayout();
        int w=(int)Math.Ceiling(ActualWidth*dpi.DpiScaleX),h=(int)Math.Ceiling(ActualHeight*dpi.DpiScaleY);
        int x=Math.Clamp(point.X-w/2,screen.Left,Math.Max(screen.Left,screen.Right-w));
        int y=VerticalPosition(point.Y,h,screen.Top,screen.Bottom,settings.ToolbarPosition);
        Native.SetWindowPos(Handle,-1,x,y,0,0,0x11);
    }
    internal static int VerticalPosition(int anchor,int height,int top,int bottom,ToolbarPlacement preference)
    {
        var above=anchor-height-14;
        var below=anchor+20;
        var y=preference==ToolbarPlacement.Below?below:above;
        if(preference==ToolbarPlacement.Below && below+height>bottom) y=above;
        else if(preference!=ToolbarPlacement.Below && above<top) y=below;
        return Math.Clamp(y,top,Math.Max(top,bottom-height));
    }
}
