using System.Windows.Interop;
using System.Windows.Media.Animation;

namespace PopBar;

internal sealed class ToolbarWindow:Window
{
    internal const int DefaultIdleOpacityPercent=35;
    internal const int MinIdleOpacityPercent=10;
    internal const int MaxIdleOpacityPercent=100;
    internal const double DefaultIdleOpacity=DefaultIdleOpacityPercent/100.0;
    readonly Border surface;
    readonly Border borderGlow;
    readonly MatrixTransform glowPosition=new();
    readonly WrapPanel buttons=new();
    readonly Func<Settings,string,IReadOnlySet<SmartActionKind>,CancellationToken,Task<IReadOnlyList<SmartAction>>> analyze;
    CancellationTokenSource? analysis;
    double idleOpacity=DefaultIdleOpacity;
    internal double IdleOpacity => idleOpacity;
    internal nint Handle => new WindowInteropHelper(this).Handle;
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Native.SetWindowLongPtr(Handle,-20,Native.GetWindowLongPtr(Handle,-20)|0x08000080);
        HwndSource.FromHwnd(Handle)?.AddHook((nint h,int msg,nint w,nint l,ref bool handled)=> {
            if(msg==0x21) { handled=true; return 3; } return 0;
        });
    }
    internal ToolbarWindow(Func<Settings,string,IReadOnlySet<SmartActionKind>,CancellationToken,Task<IReadOnlyList<SmartAction>>>? analyze=null)
    {
        this.analyze=analyze??((settings,text,missing,token)=>AiSmartAnalysis.Analyze(settings,text,missing,token));
        RefreshLanguage(); WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize;
        ShowInTaskbar=false; ShowActivated=false; Topmost=true; SizeToContent=SizeToContent.WidthAndHeight;
        AllowsTransparency=true; Background=Brushes.Transparent; Ui.Style(this); Background=Brushes.Transparent;
        surface=new Border { CornerRadius=new CornerRadius(8),BorderThickness=new Thickness(1),Padding=new Thickness(3),Margin=new Thickness(6),Child=buttons,Effect=new System.Windows.Media.Effects.DropShadowEffect { BlurRadius=12,ShadowDepth=2,Opacity=0.25,Direction=270,Color=Colors.Black } };
        surface.Name="ToolbarSurface"; surface.Opacity=DefaultIdleOpacity;
        surface.MouseEnter+=(_,_)=>Fade(1);
        surface.MouseLeave+=(_,_)=>Fade(IdleOpacity);
        surface.SetResourceReference(Border.BackgroundProperty,"SurfaceBrush");
        surface.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");
        // Keep the glow outside the surface's opacity so it stays visible at low idle settings.
        borderGlow=new Border {
            Name="ToolbarBorderGlow",CornerRadius=surface.CornerRadius,BorderThickness=surface.BorderThickness,
            Margin=surface.Margin,IsHitTestVisible=false,Visibility=Visibility.Hidden,
            OpacityMask=new RadialGradientBrush {
                MappingMode=BrushMappingMode.Absolute,Center=new Point(0,0),GradientOrigin=new Point(0,0),
                RadiusX=32,RadiusY=32,Transform=glowPosition,
                GradientStops={new GradientStop(Color.FromArgb(210,255,255,255),0),
                    new GradientStop(Color.FromArgb(110,255,255,255),0.45),new GradientStop(Colors.Transparent,1)}
            }
        };
        borderGlow.SetResourceReference(Border.BorderBrushProperty,"AccentBrush");
        borderGlow.SizeChanged+=(_,_)=> { if(borderGlow.IsVisible) AnimateBorderGlow(); };
        Content=new Grid { Children={surface,borderGlow} };
        IsVisibleChanged+=(_,_)=> {
            if(IsVisible) return;
            SetBorderGlow(false);
            CancelAnalysis();
        };
    }
    void CancelAnalysis()
    {
        var previous=analysis;
        analysis=null;
        previous?.Cancel();
    }
    internal void RefreshLanguage()
    {
        Title=T("ToolbarTitle");
        Ui.ApplyTextLanguage(this);
    }
    void Fade(double target)
    {
        surface.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(target,TimeSpan.FromMilliseconds(120)) { EasingFunction=new QuadraticEase { EasingMode=EasingMode.EaseOut } });
        SetBorderGlow(target<1);
    }
    void SetBorderGlow(bool idle)
    {
        var show=idle && IsVisible && IdleOpacity<1 && SystemParameters.ClientAreaAnimation;
        var visibility=show?Visibility.Visible:Visibility.Hidden;
        if(borderGlow.Visibility==visibility) return;
        borderGlow.Visibility=visibility;
        if(show) AnimateBorderGlow();
        else glowPosition.BeginAnimation(MatrixTransform.MatrixProperty,null);
    }
    void AnimateBorderGlow()
    {
        var inset=borderGlow.BorderThickness.Left/2;
        var width=borderGlow.ActualWidth-2*inset;
        var height=borderGlow.ActualHeight-2*inset;
        if(width<=0 || height<=0) return;
        var radius=borderGlow.CornerRadius.TopLeft-inset;
        var outline=new RectangleGeometry(new Rect(inset,inset,width,height),radius,radius);
        var path=PathGeometry.CreateFromGeometry(outline);
        path.Freeze();
        var duration=TimeSpan.FromSeconds(Math.Max(5,2*(width+height)/115));
        glowPosition.BeginAnimation(MatrixTransform.MatrixProperty,new MatrixAnimationUsingPath {
            PathGeometry=path,Duration=duration,RepeatBehavior=RepeatBehavior.Forever
        });
    }
    internal static int NormalizeIdleOpacity(int percent) =>
        Math.Clamp(percent,MinIdleOpacityPercent,MaxIdleOpacityPercent);

    static Border CreateAnalysisIndicator()
    {
        var rotation=new RotateTransform();
        var ring=new System.Windows.Shapes.Path {
            Data=Geometry.Parse("M 7,1 A 6,6 0 1 1 1,7"),
            StrokeThickness=1.8,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,
            RenderTransform=rotation,RenderTransformOrigin=new Point(0.5,0.5)
        };
        ring.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty,"AccentBrush");
        var indicator=new Border {
            Name="SmartAnalysisIndicator",Width=28,Height=28,Margin=new Thickness(1),IsHitTestVisible=false,
            Child=new Grid {
                Width=14,Height=14,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,
                Children={ring}
            }
        };
        System.Windows.Automation.AutomationProperties.SetName(indicator,T("SmartAiAnalyzing"));
        indicator.IsVisibleChanged+=(_,_)=> {
            var spin=indicator.IsVisible && SystemParameters.ClientAreaAnimation
                ? new DoubleAnimation(0,360,TimeSpan.FromMilliseconds(850)) { RepeatBehavior=RepeatBehavior.Forever }
                : null;
            rotation.BeginAnimation(RotateTransform.AngleProperty,spin);
        };
        return indicator;
    }

    internal void Present(Selection selection,Native.Point point,Settings settings,Action configure,Action<PromptAction> ai,Action<string> notify,Action<SmartAction>? transform=null,ToolbarPlacement? placement=null)
    {
        CancelAnalysis();
        idleOpacity=NormalizeIdleOpacity(settings.ToolbarIdleOpacity)/100.0;
        surface.BeginAnimation(UIElement.OpacityProperty,null); surface.Opacity=IdleOpacity;
        buttons.Children.Clear();
        void Separator() {
            if(buttons.Children.Count==0 || buttons.Children[^1] is Border) return;
            var separator=new Border { Width=1,Height=16,Margin=new Thickness(3,0,3,0),VerticalAlignment=VerticalAlignment.Center };
            separator.SetResourceReference(Border.BackgroundProperty,"BorderBrush");
            buttons.Children.Add(separator);
        }
        void Add(string glyph,string label,Action run,PromptAction? prompt=null,bool textGlyph=false) {
            var button=Ui.GlyphButton(glyph,label,()=> { Hide(); try { run(); } catch(Exception ex) { notify(ex.Message); } });
            if(textGlyph && button.Content is TextBlock icon) { icon.FontFamily=UiTypography.ForLanguage(UiText.Culture); icon.FontWeight=FontWeights.SemiBold; icon.FontSize=16; }
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
        var path=Actions.ExistingPath(selection.Text);
        if(path!=null) Add("\uE8B7",T("FileLocation"),()=>Actions.Reveal(path));
        Separator();
        foreach(var action in settings.Prompts.Take(12)) {
            Add("",action.Name,()=>ai(action),action);
        }
        Separator();
        Add("\uE713",T("Settings"),configure);
        var visibleKinds=new HashSet<SmartActionKind>();
        Border? smartDivider=null;
        void EnsureSmartDivider()
        {
            if(smartDivider!=null) return;
            smartDivider=new Border { Name="SmartActionsDivider",Width=2,Height=22,Margin=new Thickness(12,0,12,0),VerticalAlignment=VerticalAlignment.Center,Opacity=0.65 };
            smartDivider.SetResourceReference(Border.BackgroundProperty,"AccentBrush");
            buttons.Children.Add(smartDivider);
        }
        void AppendSmart(SmartAction smart)
        {
            if(!visibleKinds.Add(smart.Kind)) return;
            EnsureSmartDivider();
            var preview=smart.Source??smart.Target;
            Add(smart.Glyph,smart.Label+"\n"+(preview.Length>300?preview[..300]+"…":preview),()=> {
                if(smart.Kind==SmartActionKind.Command) smart.Run();
                else if(transform!=null) transform(smart);
                else WindowPresentation.ShowForeground(new SmartResultWindow(smart,selection,settings));
            },textGlyph:smart.Kind is SmartActionKind.Currency or SmartActionKind.Unit);
        }
        foreach(var smart in SmartActions.DetectAll(selection.Text,settings)) AppendSmart(smart);

        var analyzeSelection=settings.AiSmartAnalysis && settings.HasAiConfiguration &&
            !string.IsNullOrWhiteSpace(selection.Text) && selection.Text.Length<=AiSmartAnalysis.MaxInputLength &&
            link==null && !Path.IsPathFullyQualified(selection.Text.Trim().Trim('"')) &&
            !visibleKinds.Contains(SmartActionKind.Command);
        var missing=AiSmartAnalysis.Supported.Where(kind=>analyzeSelection && !visibleKinds.Contains(kind) &&
            (kind!=SmartActionKind.Currency || settings.CurrencyConversion)).ToHashSet();
        Border? indicator=null;
        if(missing.Count>0) {
            EnsureSmartDivider();
            indicator=CreateAnalysisIndicator();
            buttons.Children.Add(indicator);
        }

        var screen=System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(point.X,point.Y)).WorkingArea;
        buttons.MaxWidth=Math.Min(850,screen.Width*0.75);
        // Position using physical pixels after WPF has measured the monitor-specific DPI.
        Show(); Native.SetWindowPos(Handle,-1,point.X,point.Y,0,0,0x11); UpdateLayout();
        var dpi=VisualTreeHelper.GetDpi(this);
        buttons.MaxWidth=Math.Max(100,Math.Min(850,(screen.Width-16)/dpi.DpiScaleX));
        int? left=null;
        void Position()
        {
            UpdateLayout();
            int w=(int)Math.Ceiling(ActualWidth*dpi.DpiScaleX),h=(int)Math.Ceiling(ActualHeight*dpi.DpiScaleY);
            left=Math.Clamp(left??(point.X-w/2),screen.Left,Math.Max(screen.Left,screen.Right-w));
            int y=VerticalPosition(point.Y,h,screen.Top,screen.Bottom,placement??settings.ToolbarPosition);
            Native.SetWindowPos(Handle,-1,left.Value,y,0,0,0x11);
        }
        Position();
        SetBorderGlow(true);
        if(indicator!=null) _=AppendAnalyzed(indicator);

        async Task AppendAnalyzed(Border pendingIndicator)
        {
            using var request=new CancellationTokenSource(TimeSpan.FromSeconds(15));
            analysis=request;
            IReadOnlyList<SmartAction> suggestions=Array.Empty<SmartAction>();
            try {
                // Let WPF render the local toolbar and avoid sending selections immediately superseded by input.
                await Task.Delay(100,request.Token).ConfigureAwait(false);
                suggestions=await analyze(settings,selection.Text,missing,request.Token).ConfigureAwait(false);
            } catch {
                // Background analysis is optional; the toolbar remains usable when the service fails.
            }
            if(Dispatcher.HasShutdownStarted) return;
            try {
                await Dispatcher.InvokeAsync(()=> {
                    if(analysis!=request) return;
                    analysis=null;
                    buttons.Children.Remove(pendingIndicator);
                    if(!request.IsCancellationRequested && IsVisible &&
                       (selection.Window==0 || Native.GetForegroundWindow()==selection.Window)) {
                        foreach(var suggestion in suggestions)
                            if(missing.Contains(suggestion.Kind)) AppendSmart(suggestion);
                    }
                    if(visibleKinds.Count==0 && smartDivider!=null) buttons.Children.Remove(smartDivider);
                    if(IsVisible) Position();
                }).Task.ConfigureAwait(false);
            } catch(OperationCanceledException) {
                // The dispatcher can shut down while the update is queued.
            }
        }
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
