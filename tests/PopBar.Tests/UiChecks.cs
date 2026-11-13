using PopBar;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;

internal static class UiChecks
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool PrintWindow(nint window,nint dc,uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern void mouse_event(uint flags,uint x,uint y,uint data,nuint extra);
    [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="SendMessageW")]
    static extern nint SendMessage(nint window,uint message,nint wParam,nint lParam);
    public static int Run()
    {
        var passed=0;
        Exception? error=null;
        var thread=new Thread(()=> {
            try {
                var task=RunOnUiThread();
                while(!task.IsCompleted) {
                    var frame=new System.Windows.Threading.DispatcherFrame();
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.Background,new Action(()=>frame.Continue=false));
                    System.Windows.Threading.Dispatcher.PushFrame(frame);
                }
                passed=task.GetAwaiter().GetResult();
            } catch(Exception ex) { error=ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if(error!=null) throw new Exception("UI checks failed",error);
        return passed;
    }

    static async Task<int> RunOnUiThread()
    {
        var testFont=Environment.GetEnvironmentVariable("POPBAR_TEST_NERD_FONT");
        var testFamily=string.IsNullOrEmpty(testFont)?null:Fonts.GetFontFamilies(System.IO.Path.GetDirectoryName(testFont)!+System.IO.Path.DirectorySeparatorChar).First();
        var passed=0;
        var app=new App { StartServices=false };
        app.InitializeComponent();
        void Check(bool yes,string name) { if(!yes) throw new Exception(name); Console.WriteLine("PASS "+name); passed++; }
        var plainDocument=new System.Windows.Documents.FlowDocument(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("第一段")));
        plainDocument.Blocks.Add(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("第二段")));
        Check(MarkdownText.Plain(plainDocument)=="第一段\r\n第二段","markdown plain text extraction");
        Check(MarkdownText.Plain(null)=="","markdown plain text handles empty document");
        var brushKeys=new[] { "WindowBackgroundBrush","SurfaceBrush","SecondarySurfaceBrush","TextBrush","MutedTextBrush","BorderBrush","AccentBrush","AccentHoverBrush","AccentPressedBrush","OnAccentBrush","DangerBrush","HoverBrush","PressedBrush","DisabledBrush","FocusBrush" };
        var styleKeys=new[] { "PrimaryButtonStyle","SecondaryButtonStyle","SubtleButtonStyle","DangerButtonStyle","ToolbarButtonStyle","NavigationButtonStyle","InputTextBoxStyle","InputPasswordBoxStyle","CheckBoxStyle","ComboBoxStyle","ToolTipStyle" };

        ThemeManager.Apply(AppTheme.Light);
        var light=((SolidColorBrush)app.FindResource("WindowBackgroundBrush")).Color;
        Check(brushKeys.All(key=>app.FindResource(key) is SolidColorBrush),"light theme brush resources");
        Check(styleKeys.All(key=>app.FindResource(key) is Style),"shared control styles");
        ThemeManager.Apply(AppTheme.Dark);
        var dark=((SolidColorBrush)app.FindResource("WindowBackgroundBrush")).Color;
        Check(brushKeys.All(key=>app.FindResource(key) is SolidColorBrush),"dark theme brush resources");
        Check(light!=dark,"light and dark surfaces differ");

        var existing=new Window();
        Ui.Style(existing);
        ThemeManager.Apply(AppTheme.Light);
        var before=((SolidColorBrush)existing.Background).Color;
        ThemeManager.Apply(AppTheme.Dark);
        Check(((SolidColorBrush)existing.Background).Color!=before,"existing window updates theme");
        var created=new Window();
        Ui.Style(created);
        Check(((SolidColorBrush)created.Background).Color==((SolidColorBrush)app.FindResource("WindowBackgroundBrush")).Color,"new window inherits theme");
        var existingHandle=new System.Windows.Interop.WindowInteropHelper(existing).EnsureHandle();
        var createdHandle=new System.Windows.Interop.WindowInteropHelper(created).EnsureHandle();
        Check(existing.Icon!=null && created.Icon!=null,"windows use embedded PopBar icon");
        if(OperatingSystem.IsWindowsVersionAtLeast(10,0,22000)) {
            existing.Title="PopBar · 标题栏主题"; existing.Width=420; existing.Height=160; existing.ShowActivated=false; existing.Show();
            created.Width=320; created.Height=160; created.ShowActivated=false; created.Show();
            foreach(var preference in new[]{AppTheme.Light,AppTheme.Dark,AppTheme.System}) {
                ThemeManager.Apply(preference);
                Check(ThemeManager.ApplyTitleBar(existing) && ThemeManager.ApplyTitleBar(created),$"DWM accepts {preference} title bar colors");
                var folder=Environment.GetEnvironmentVariable("POPBAR_UI_ARTIFACTS");
                if(!string.IsNullOrEmpty(folder)) {
                    System.IO.Directory.CreateDirectory(folder);
                    existing.UpdateLayout();
                    var dpi=VisualTreeHelper.GetDpi(existing);
                    using var bitmap=new System.Drawing.Bitmap((int)(existing.ActualWidth*dpi.DpiScaleX),(int)(existing.ActualHeight*dpi.DpiScaleY));
                    using(var graphics=System.Drawing.Graphics.FromImage(bitmap)) {
                        var dc=graphics.GetHdc();
                        try { Check(PrintWindow(existingHandle,dc,2),$"capture native {preference} title bar"); }
                        finally { graphics.ReleaseHdc(dc); }
                    }
                    bitmap.Save(System.IO.Path.Combine(folder,$"native-titlebar-{preference}.png"));
                }
            }
        }
        using(var stream=Application.GetResourceStream(new Uri("pack://application:,,,/PopBar;component/Assets/PopBar.ico")).Stream)
        using(var icon=new System.Drawing.Icon(stream,32,32))
            Check(icon.Width==32 && icon.Height==32,"embedded tray icon loads at 32 pixels");
        existing.Close(); created.Close();

        var button=Ui.Button("保存",()=>{});
        var input=Ui.Input("",false);
        var glyph=Ui.GlyphButton("\uE8D4","复制",()=>{});
        Check(ReferenceEquals(button.Style,app.FindResource("SecondaryButtonStyle")),"shared button style");
        Check(ReferenceEquals(input.Style,app.FindResource("InputTextBoxStyle")),"shared input style");
        Check(ReferenceEquals(glyph.Style,app.FindResource("ToolbarButtonStyle")),"shared glyph button style");
        Check(glyph.ToolTip as string=="复制" && AutomationProperties.GetName(glyph)=="复制","glyph button accessible name");
        var emojiPrompt=new PromptAction { Name="自定义",IconKind=PromptIconKind.Emoji,Icon="👩‍💻" };
        var emojiIcon=PromptIcons.Create(emojiPrompt,out var emojiWarning);
        Check(emojiIcon.Text=="👩‍💻" && emojiWarning=="" && emojiIcon.FontFamily.Source.Contains("Emoji"),"emoji ZWJ sequence uses emoji font");
        Check(PromptIcons.Glyph(new PromptAction { IconKind=PromptIconKind.Emoji,Icon="👍🏽" })=="👍🏽","emoji modifier remains one icon");
        Check(PromptIcons.Glyph(new PromptAction { IconKind=PromptIconKind.Emoji,Icon="😀😀" })=="","multiple icons rejected");
        Check(PromptIcons.Glyph(new PromptAction { IconKind=PromptIconKind.NerdFont,Icon="U+F121" })=="\uF121" &&
            PromptIcons.Glyph(new PromptAction { IconKind=PromptIconKind.NerdFont,Icon="0xF0001" })==char.ConvertFromUtf32(0xF0001),"nerd BMP and supplementary code points");
        Check(PromptIcons.Glyph(new PromptAction { IconKind=PromptIconKind.NerdFont,Icon="U+110000" })=="","invalid code point rejected");
        foreach(var value in new[]{@"\udb81\uddca",@"\uDB81\uDDCA",@"\U000F05CA","U+F05CA","0xF05CA","F05CA",char.ConvertFromUtf32(0xF05CA)})
            Check(PromptIcons.Glyph(new PromptAction { IconKind=PromptIconKind.NerdFont,Icon=value })==char.ConvertFromUtf32(0xF05CA),$"nerd supplementary input {value}");
        Check(PromptIcons.Glyph(new PromptAction { IconKind=PromptIconKind.NerdFont,Icon=@" \uF121 " })=="\uF121","nerd escaped BMP with whitespace");
        foreach(var value in new[]{@"\udb81",@"\uddca",@"\uddca\udb81",@"\udb81\uF121",@"\uF121\uF122",@"\uXYZ1",@"\uF12",@"\U00110000",@"\U0000D800","\uDB81","\uDDCA",""})
            Check(PromptIcons.Glyph(new PromptAction { IconKind=PromptIconKind.NerdFont,Icon=value })=="",$"invalid nerd input rejected {value}");
        var missing=PromptIcons.Create(new PromptAction { Name="翻译",IconKind=PromptIconKind.NerdFont,Icon="F121",IconFont="Missing Nerd Font For Test" },out var missingWarning);
        Check(missing.Text=="✨" && missing.FontFamily==UiTypography.Emoji && missingWarning.Length>0,"missing Nerd Font falls back to emoji");
        var restored=System.Text.Json.JsonSerializer.Deserialize<PromptAction>(System.Text.Json.JsonSerializer.Serialize(emojiPrompt.Copy()))!;
        Check(restored.IconKind==PromptIconKind.Emoji && restored.Icon==emojiPrompt.Icon,"icon draft copy and JSON round trip");
        Check(System.Text.Json.JsonSerializer.Deserialize<PromptAction>("{}")!.IconKind==PromptIconKind.Emoji,"legacy prompts migrate to emoji");
        foreach(var json in new[]{"{\"Name\":\"翻译\"}","{\"Name\":\"翻译\",\"IconKind\":\"Default\",\"Icon\":\"\"}","{\"Name\":\"翻译\",\"IconKind\":0}"}) {
            var migrated=System.Text.Json.JsonSerializer.Deserialize<PromptAction>(json)!;
            Check(migrated.IconKind==PromptIconKind.Emoji && migrated.Icon=="🌐","legacy built-in icon migrates to explicit emoji");
            migrated.Name="Renamed";
            Check(PromptIcons.Create(migrated,out _).Text=="🌐","renaming does not change migrated icon");
        }
        Check(BuiltInPrompts.Create().All(action=>action.IconKind==PromptIconKind.Emoji && PromptIcons.Glyph(action).Length>0),"preset buttons store emoji icons");
        foreach(var family in new[]{"JetBrainsMono NF","JetBrainsMono NFM","JetBrainsMono NFP","JetBrainsMonoNL NF","JetBrainsMonoNL NFM","JetBrainsMonoNL NFP","jetbrainsmono nf"})
            Check(PromptIcons.IsNerdFontFamily(family),$"detect Nerd Font family {family}");
        foreach(var family in new[]{"Segoe UI","JetBrains Mono","Maple Mono","NFranklin","Info Font","Maple Mono NF CN","Maple Mono Normal NF CN","Symbols Nerd Font"})
            Check(!PromptIcons.IsNerdFontFamily(family),$"exclude unpatched family {family}");
        Console.WriteLine("Detected Nerd Fonts: "+string.Join(", ",PromptIcons.NerdFonts));
        var catalog=NerdIconCatalog.Entries;
        Check(catalog.Length>10000 && catalog.All(item=>PromptIcons.Glyph(new PromptAction { IconKind=PromptIconKind.NerdFont,Icon=item.Glyph })==item.Glyph),"bundled nerd catalog contains valid glyphs");
        Check(NerdIconCatalog.Search(catalog,"MD TRANSLATE").Any(item=>item.Code==0xF05CA),"nerd search accepts case-insensitive name terms");
        foreach(var query in new[]{"U+F05CA","0xf05ca",@"\udb81\uddca",char.ConvertFromUtf32(0xF05CA)})
            Check(NerdIconCatalog.Search(catalog,query).Any(item=>item.Code==0xF05CA),"nerd search accepts code point or pasted glyph");
        var picker=testFamily==null?new NerdIconPicker((string?)null):new NerdIconPicker(testFamily);
        picker.Show(); picker.UpdateLayout();
        Capture(picker,"nerd-icon-picker");
        var pickerSearch=FindNamed<TextBox>(picker,"NerdIconSearch");
        pickerSearch.Text="no_such_icon_for_test";
        Check(FindNamed<TextBlock>(picker,"NerdIconStatus").Text.Contains("没有匹配"),"nerd picker empty search state");
        picker.Close();
        Check(picker.SelectedGlyph==null,"closing nerd picker leaves selection unchanged");
        if(testFamily!=null || PromptIcons.NerdFonts.Length>0) {
            var selectionPicker=testFamily==null?new NerdIconPicker((string?)null):new NerdIconPicker(testFamily);
            selectionPicker.Loaded+=(_,_)=>selectionPicker.Dispatcher.BeginInvoke(new Action(()=> {
                var tile=Descendants<Button>(selectionPicker).FirstOrDefault(button=>button.ToolTip is string tip && tip.Contains("U+"));
                if(tile==null) selectionPicker.Close(); else tile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }));
            Check(selectionPicker.ShowDialog()==true && selectionPicker.SelectedGlyph!=null,"nerd picker selects a supported glyph");
        }

        foreach(var orientation in new[]{Orientation.Vertical,Orientation.Horizontal}) {
            var bar=new System.Windows.Controls.Primitives.ScrollBar { Orientation=orientation,Minimum=0,Maximum=100,Value=40,ViewportSize=10 };
            bar.Style=(Style)app.FindResource(typeof(System.Windows.Controls.Primitives.ScrollBar));
            bar.ApplyTemplate();
            var track=(System.Windows.Controls.Primitives.Track)bar.Template.FindName("PART_Track",bar);
            Check(track.IsDirectionReversed==(orientation==Orientation.Vertical),$"{orientation} scrollbar direction");
            Check(track.DecreaseRepeatButton.Command==(orientation==Orientation.Vertical?System.Windows.Controls.Primitives.ScrollBar.PageUpCommand:System.Windows.Controls.Primitives.ScrollBar.PageLeftCommand)
                && track.IncreaseRepeatButton.Command==(orientation==Orientation.Vertical?System.Windows.Controls.Primitives.ScrollBar.PageDownCommand:System.Windows.Controls.Primitives.ScrollBar.PageRightCommand),$"{orientation} scrollbar page commands");
        }
        Check(ThemeManager.Resolve((AppTheme)99,true)==AppTheme.Light && ThemeManager.Resolve((AppTheme)99,false)==AppTheme.Dark,"unknown numeric theme safely follows system");

        void PumpFor(int milliseconds)
        {
            var frame=new System.Windows.Threading.DispatcherFrame();
            var timer=new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background,app.Dispatcher) { Interval=TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick+=(_,_)=> { timer.Stop(); frame.Continue=false; };
            timer.Start();
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
        var toolbarCursor=System.Windows.Forms.Cursor.Position;
        var toolbar=new ToolbarWindow();
        try {
            System.Windows.Forms.Cursor.Position=new System.Drawing.Point(0,0);
            toolbar.Present(new Selection(0,"hello",false,null,null),new Native.Point { X=100,Y=100 },new Settings(),()=>{},_=>{},_=>{});
            var toolbarSurface=FindNamed<Border>(toolbar,"ToolbarSurface");
            Check(Math.Abs(toolbarSurface.Opacity-ToolbarWindow.DefaultIdleOpacity)<0.001,"toolbar starts semi-transparent");
            var borderGlow=FindNamed<Border>(toolbar,"ToolbarBorderGlow");
            var glowPosition=(MatrixTransform)borderGlow.OpacityMask.Transform;
            var animationsEnabled=SystemParameters.ClientAreaAnimation;
            Check(!borderGlow.IsHitTestVisible && borderGlow.IsVisible==animationsEnabled &&
                glowPosition.HasAnimatedProperties==animationsEnabled,"idle glow respects system animation settings and lets clicks through");
            if(animationsEnabled) {
                PumpFor(50);
                var startingPosition=glowPosition.Matrix;
                PumpFor(200);
                Check(glowPosition.Matrix!=startingPosition,"idle glow moves along the border");
            }
            var panel=Descendants<WrapPanel>(toolbar).Single();
            var buttons=panel.Children.OfType<Button>().ToList();
            var names=new[] { "复制","搜索","翻译","解释","润色","设置" };
            Check(buttons.Count==names.Length && buttons.All(button=>button.Content is TextBlock or Viewbox),"toolbar uses icon-only buttons");
            Check(buttons.Skip(2).Take(3).Select(button=>((TextBlock)((Viewbox)button.Content).Child).Text).SequenceEqual(new[]{"🌐","💡","✍️"}),"toolbar renders preset emoji icons");
            Check(buttons.Select(button=>button.ToolTip as string).SequenceEqual(names) && buttons.Select(AutomationProperties.GetName).SequenceEqual(names),"toolbar button names and order");
            Check(buttons.All(button=>!button.Focusable) && !toolbar.ShowActivated,"toolbar does not take focus");
            Check(panel.Children.OfType<Border>().Any(border=>border.Width==1),"toolbar visual group separators");
            Check(!panel.Children.OfType<Border>().Any(border=>border.Name=="SmartActionsDivider"),"ordinary text has no smart group");
            Check(!buttons.Any(button=>new[]{"剪切","粘贴","替换原文"}.Contains(button.ToolTip as string)),"read-only selection hides edit actions");
            Capture(toolbar,"toolbar");
            toolbarSurface.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0) { RoutedEvent=System.Windows.Input.Mouse.MouseEnterEvent });
            PumpFor(250);
            Check(Math.Abs(toolbarSurface.Opacity-1)<0.001,"toolbar hover becomes opaque");
            Check(!borderGlow.IsVisible && !glowPosition.HasAnimatedProperties,"hover stops the border glow");
            toolbar.Present(new Selection(0,"hello",false,null,null),new Native.Point { X=100,Y=100 },new Settings { ToolbarIdleOpacity=65,Prompts=new(){emojiPrompt} },()=>{},_=>{},_=>{});
            Check(!toolbarSurface.HasAnimatedProperties && Math.Abs(toolbarSurface.Opacity-0.65)<0.001,
                "toolbar presentation resets to the configured idle opacity");
            var custom=Descendants<Button>(toolbar).Single(item=>AutomationProperties.GetName(item)=="自定义");
            Check(custom.Content is Viewbox box && box.Child is TextBlock text && text.Text=="👩‍💻" && !custom.Focusable,"toolbar renders custom emoji without focus");
            toolbarSurface.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0) { RoutedEvent=System.Windows.Input.Mouse.MouseEnterEvent });
            PumpFor(250);
            Check(Math.Abs(toolbarSurface.Opacity-1)<0.001,"toolbar hover becomes opaque after present");
            toolbarSurface.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0) { RoutedEvent=System.Windows.Input.Mouse.MouseLeaveEvent });
            PumpFor(250);
            Check(Math.Abs(toolbarSurface.Opacity-0.65)<0.001,"toolbar leave returns to the configured idle opacity");
            Check(borderGlow.IsVisible==animationsEnabled && glowPosition.HasAnimatedProperties==animationsEnabled,"mouse leave restores the idle glow");
            Capture(toolbar,"toolbar-emoji");
            toolbar.Present(new Selection(0,"https://example.com/docs",false,null,null),new Native.Point(100,100),new Settings(),()=>{},_=>{},_=>{});
            Check(Descendants<Button>(toolbar).Count(button=>(button.ToolTip as string)=="打开链接")==1 && !Descendants<Button>(toolbar).Any(button=>(button.ToolTip as string)?.Contains('\n')==true),"URL uses only the original link button");
            Check(!panel.Children.OfType<Border>().Any(border=>border.Name=="SmartActionsDivider"),"no empty smart group divider for URLs");
            toolbar.Present(new Selection(0,AppContext.BaseDirectory,false,null,null),new Native.Point(100,100),new Settings(),()=>{},_=>{},_=>{});
            Check(Descendants<Button>(toolbar).Count(button=>(button.ToolTip as string)=="文件位置")==1 && !Descendants<Button>(toolbar).Any(button=>(button.ToolTip as string)?.Contains('\n')==true),"path uses only the original location button");
            foreach(var sample in new[]{("git status","执行命令（CMD）\ngit status"),("Get-ChildItem","执行命令（PowerShell）\nGet-ChildItem")}) {
                toolbar.Present(new Selection(0,sample.Item1,false,null,null),new Native.Point(100,100),new Settings(),()=>{},_=>{},_=>{});
                Check(Descendants<Button>(toolbar).Any(button=>(button.ToolTip as string)==sample.Item2),"smart toolbar action previews its target: "+sample.Item1);
            }
            Capture(toolbar,"toolbar-smart-command");
            toolbar.Present(new Selection(0,"72°F",false,null,null),new Native.Point(100,100),new Settings(),()=>{},_=>{},_=>{});
            var unitButtons=Descendants<Button>(toolbar).ToList();
            Check((unitButtons[^1].ToolTip as string)!.StartsWith("单位转换\n")&&(unitButtons[^2].ToolTip as string)=="设置","all smart buttons occupy the final toolbar positions");
            var smartDivider=panel.Children.OfType<Border>().Single(border=>border.Name=="SmartActionsDivider");
            Check(smartDivider.Width==2&&smartDivider.Margin.Left>=12&&panel.Children.IndexOf(smartDivider)>panel.Children.IndexOf(unitButtons[^2]),"smart group follows settings with a distinct wide divider");
            PumpFor(50);
            Capture(toolbar,"toolbar-smart-units");
            toolbar.Present(new Selection(0,"500百万円",false,null,null),new Native.Point(100,100),new Settings(),()=>{},_=>{},_=>{});
            var currencyIcon=(TextBlock)Descendants<Button>(toolbar).Single(button=>(button.ToolTip as string)!.StartsWith("汇率转换\n")).Content;
            Check(currencyIcon.Text=="¥"&&currencyIcon.FontFamily.Equals(UiTypography.ForLanguage(UiText.Culture)),"currency icon uses a regular font currency symbol");
            PumpFor(50); Capture(toolbar,"toolbar-currency-icon");
            toolbar.Present(new Selection(0,"ordinary text",false,null,null),new Native.Point(100,100),new Settings(),()=>{},_=>{},_=>{});
            Check(!Descendants<Button>(toolbar).Any(button=>(button.ToolTip as string)?.StartsWith("执行命令")==true),"smart buttons refresh when the selection changes");
            toolbar.Hide();
            Check(!borderGlow.IsVisible && !glowPosition.HasAnimatedProperties,"hiding the toolbar stops the glow animation");
            toolbar.Present(new Selection(0,"hello",false,null,null),new Native.Point(100,100),new Settings { ToolbarIdleOpacity=100 },()=>{},_=>{},_=>{});
            Check(!borderGlow.IsVisible && !glowPosition.HasAnimatedProperties,"fully opaque toolbar has no glow");
        } finally { toolbar.Close(); System.Windows.Forms.Cursor.Position=toolbarCursor; }

        void CheckResultChrome(ResultPopupWindow window)
        {
            var surface=FindNamed<Border>(window,"ResultSurface");
            var caption=FindNamed<Border>(window,"ResultTitleBar");
            var close=FindNamed<Button>(window,"CloseResultButton");
            var hwnd=new System.Windows.Interop.WindowInteropHelper(window).Handle;
            nint HitTest(FrameworkElement element,Point point)
            {
                var screen=element.PointToScreen(point);
                var coordinates=((int)screen.X & 0xffff) | ((int)screen.Y << 16);
                return SendMessage(hwnd,0x84,0,coordinates); // WM_NCHITTEST
            }
            Check(surface.CornerRadius.TopLeft==12 && !surface.Clip.FillContains(new Point(1,1)),"result surface clips its corners");
            Check(HitTest(caption,new Point(caption.ActualWidth/2,caption.ActualHeight/2))==2 &&
                HitTest(surface,new Point(2,surface.ActualHeight/2))==10 &&
                HitTest(close,new Point(close.ActualWidth/2,close.ActualHeight/2))==1,
                "result caption drags, edge resizes and close remains clickable");
        }
        var roundedResult=new ResultWindow(new Settings { Model="test-model" },new PromptAction { Name="翻译",Prompt="{{text}}" },
            new Selection(0,new string('x',117),false,null,null),(_,_,append,_,_)=> { append("会议改到周四下午三点。"); return Task.CompletedTask; });
        try {
            roundedResult.Show(); PumpFor(40);
            CheckResultChrome(roundedResult);
            Check(FindNamed<TextBlock>(roundedResult,"ResultHeading").Text=="翻译" &&
                FindNamed<TextBlock>(roundedResult,"ResultModel").Text=="test-model" &&
                LogicalDescendants<TextBlock>(roundedResult).All(text=>!text.Text.Contains("117")),
                "AI result header shows the action and model without the selected character count");
            Capture(roundedResult,"rounded-ai-result");
            FindNamed<Button>(roundedResult,"FollowUpButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Capture(roundedResult,"rounded-ai-follow-up");
            roundedResult.Width=roundedResult.MinWidth;
            roundedResult.Height=roundedResult.MinHeight;
            roundedResult.UpdateLayout();
            CheckResultChrome(roundedResult);
            FindNamed<Button>(roundedResult,"CloseResultButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!roundedResult.IsVisible,"result caption close button closes the window");
        } finally { if(roundedResult.IsVisible) roundedResult.Close(); }

        foreach(var (kind,smartInput,expected) in new[]{
            (SmartActionKind.Calculate,"128*0.85","108.8"),
            (SmartActionKind.Unit,"32°F","= 0 °C"),
            (SmartActionKind.Json,"{\"a\":1}","\n")
        }) {
            var smartPreview=new SmartResultWindow(new SmartAction(kind,smartInput),new Selection(0,smartInput,false,null,null),new Settings());
            try {
                smartPreview.Show(); PumpFor(50);
                CheckResultChrome(smartPreview);
                Check(FindNamed<TextBox>(smartPreview,"SmartOutput").Text.Contains(expected),"smart preview renders "+kind);
                Check(FindNamed<Button>(smartPreview,"SmartCopy").IsEnabled&&FindNamed<Button>(smartPreview,"SmartReplace").Visibility==Visibility.Collapsed,"smart preview enables copy and protects read-only source "+kind);
                Capture(smartPreview,"smart-preview-"+kind);
                var source=(PresentationSource)PresentationSource.FromVisual(smartPreview);
                smartPreview.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,source,0,System.Windows.Input.Key.Escape) {
                    RoutedEvent=System.Windows.Input.Keyboard.KeyDownEvent
                });
                Check(!smartPreview.IsVisible,"Escape closes the smart preview "+kind);
            } finally { if(smartPreview.IsVisible) smartPreview.Close(); }
        }
        var invalidPreview=new SmartResultWindow(new SmartAction(SmartActionKind.Calculate,"1/0"),new Selection(0,"1/0",true,null,null),new Settings());
        try {
            invalidPreview.Show(); PumpFor(20);
            Check(!FindNamed<Button>(invalidPreview,"SmartCopy").IsEnabled&&!FindNamed<Button>(invalidPreview,"SmartReplace").IsEnabled&&FindNamed<TextBlock>(invalidPreview,"SmartStatus").Text.StartsWith("转换失败"),"failed calculation disables stale copy and replace");
        } finally { invalidPreview.Close(); }

        var currencyPreview=new SmartResultWindow(new SmartAction(SmartActionKind.Currency,"$100"),new Selection(0,"$100",true,null,null),new Settings(),
            (amounts,target,token)=>target=="USD"?Task.FromException<string>(new IOException("offline")):
                CurrencyConversion.Convert(amounts,target,token,(from,to,ct)=>Task.FromResult(new CurrencyRate(from,to,to=="JPY"?150:7.25m,new DateOnly(2026,1,2)))));
        try {
            currencyPreview.Show(); PumpFor(30);
            Check(FindNamed<TextBox>(currencyPreview,"SmartOutput").Text.Contains("725.00 CNY")&&FindNamed<Button>(currencyPreview,"SmartReplace").IsEnabled,"currency preview renders referenced result for editable source");
            FindNamed<ComboBox>(currencyPreview,"SmartMode").SelectedItem="JPY";
            Check(FindNamed<TextBox>(currencyPreview,"SmartOutput").Text.Contains("15,000 JPY"),"currency preview target switches");
            Capture(currencyPreview,"smart-preview-Currency");
            FindNamed<ComboBox>(currencyPreview,"SmartMode").SelectedItem="USD";
            Check(FindNamed<TextBox>(currencyPreview,"SmartOutput").Text.Length==0&&!FindNamed<Button>(currencyPreview,"SmartCopy").IsEnabled&&FindNamed<TextBlock>(currencyPreview,"SmartStatus").Text.Contains("失败"),"network failure clears stale currency result and disables copy");
        } finally { currencyPreview.Close(); }

        const string aiMoneyText="売上は五億円です。";
        var aiMoneySettings=new Settings { Model="test-model" };
        var aiMoneyAction=new SmartAction(SmartActionKind.Currency,aiMoneyText) {
            Amounts=new[]{new MoneyAmount(500000000m,"JPY","五億円",false)}
        };
        var aiMoneyPreview=new SmartResultWindow(aiMoneyAction,new Selection(0,aiMoneyText,false,null,null),aiMoneySettings,
            (amounts,target,token)=>CurrencyConversion.Convert(amounts,target,token,
                (from,to,_)=>Task.FromResult(new CurrencyRate(from,to,0.05m,new DateOnly(2026,1,2)))));
        try {
            aiMoneyPreview.Show(); PumpFor(30);
            var aiOutput=FindNamed<TextBox>(aiMoneyPreview,"SmartOutput");
            Check(aiOutput.Text.Contains("五億円")&&aiOutput.Text.Contains("25,000,000.00 CNY"),"AI money preview shows source evidence and conversion");
            FindNamed<ComboBox>(aiMoneyPreview,"SmartMode").SelectedItem="JPY";
            Check(aiOutput.Text.Contains("500,000,000 JPY"),"switching currencies reuses the AI extraction");
        } finally { aiMoneyPreview.Close(); }

        var firstAnalysis=new TaskCompletionSource<IReadOnlyList<SmartAction>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lateAnalysis=new TaskCompletionSource<IReadOnlyList<SmartAction>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lateToken=new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var aiToolbar=new ToolbarWindow((_,text,_,token)=> {
            if(text==aiMoneyText) return firstAnalysis.Task;
            if(text=="no matches") return Task.FromResult<IReadOnlyList<SmartAction>>(Array.Empty<SmartAction>());
            if(text=="analysis failed") return Task.FromException<IReadOnlyList<SmartAction>>(new IOException("offline"));
            lateToken.TrySetResult(token);
            return lateAnalysis.Task;
        });
        try {
            SmartAction? clicked=null;
            var point=new Native.Point(100,100);
            aiToolbar.Present(new Selection(0,aiMoneyText,false,null,null),point,aiMoneySettings,()=>{},_=>{},_=>{},action=>clicked=action);
            Check(aiToolbar.IsVisible&&!Descendants<Button>(aiToolbar).Any(button=>(button.ToolTip as string)!.StartsWith("汇率转换")),"toolbar appears before the AI result");
            var aiPanel=Descendants<WrapPanel>(aiToolbar).Single();
            Check(aiPanel.Children[^1] is Border { Name:"SmartAnalysisIndicator",IsVisible:true },"analysis indicator occupies the rightmost slot");
            firstAnalysis.SetResult(new[]{aiMoneyAction});
            PumpFor(300);
            var moneyButton=Descendants<Button>(aiToolbar).Last();
            Check((moneyButton.ToolTip as string)!.StartsWith("汇率转换") &&
                  !aiPanel.Children.OfType<Border>().Any(border=>border.Name=="SmartAnalysisIndicator"),"currency button replaces the analysis indicator");
            moneyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(clicked==aiMoneyAction,"appended button uses the extracted amounts");

            foreach(var text in new[]{"no matches","analysis failed"}) {
                aiToolbar.Present(new Selection(0,text,false,null,null),point,aiMoneySettings,()=>{},_=>{},_=>{});
                PumpFor(300);
                Check(!aiPanel.Children.OfType<Border>().Any(border=>border.Name is "SmartAnalysisIndicator" or "SmartActionsDivider"),
                    "analysis indicator and empty divider are cleared: "+text);
            }

            aiToolbar.Present(new Selection(0,"三万米",false,null,null),point,aiMoneySettings,()=>{},_=>{},_=>{});
            PumpFor(300);
            aiToolbar.Present(new Selection(0,"new selection",false,null,null),point,new Settings(),()=>{},_=>{},_=>{});
            Check(lateToken.Task.IsCompletedSuccessfully&&lateToken.Task.Result.IsCancellationRequested,"new selection cancels old analysis");
            lateAnalysis.SetResult(new[]{aiMoneyAction});
            PumpFor(100);
            Check(!Descendants<Button>(aiToolbar).Any(button=>(button.ToolTip as string)!.StartsWith("汇率转换")) &&
                  !aiPanel.Children.OfType<Border>().Any(border=>border.Name=="SmartAnalysisIndicator"),"late results cannot add buttons or an indicator to a newer selection");
        } finally { aiToolbar.Close(); }

        var languageSettings=new Settings();
        var builtIn=languageSettings.Prompts[0];
        var originalTemplate=builtIn.Prompt;
        var customPrompt=new PromptAction { Name="我的提示词",Prompt="Keep this template: {{text}}" };
        var languagePreview=new ResultWindow(languageSettings,builtIn,new Selection(0,"hello",false,null,null));
        try {
            foreach(var (choice,tag,title,promptName,copyLabel) in new[]{
                (UiLanguage.English,"en-US","PopBar · Settings","Translate","Copy"),
                (UiLanguage.Japanese,"ja-JP","PopBar · 設定","翻訳","コピー")
            }) {
                UiText.Apply(choice);
                languagePreview.RefreshLanguage();
                BuiltInPrompts.LocalizeName(customPrompt);
                languageSettings.Language=choice;
                var localizedSettings=new SettingsWindow(languageSettings,_=>{});
                try {
                    var selector=FindNamed<ComboBox>(localizedSettings,"LanguageSelector");
                    Check(localizedSettings.Title==title && string.Equals(localizedSettings.Language.IetfLanguageTag,tag,StringComparison.OrdinalIgnoreCase) &&
                        Equals(selector.SelectedValue,choice),$"settings display and select {tag}");
                    Check(languagePreview.Title.EndsWith(promptName,StringComparison.Ordinal) &&
                        Equals(FindNamed<Button>(languagePreview,"CopyResultButton").Content,copyLabel) &&
                        builtIn.Prompt==originalTemplate && customPrompt.Name=="我的提示词",$"open result switches to {tag} without changing prompt text");
                    languagePreview.RenderMarkdown("# 表示言語\n\n本文と `設定`\n\n```\n表示言語\n```");
                    var document=FindNamed<MdXaml.MarkdownScrollViewer>(languagePreview,"OutputMarkdown").Document!;
                    Check(document.FontFamily.Source==localizedSettings.FontFamily.Source &&
                        document.Blocks.OfType<System.Windows.Documents.Paragraph>().Single(block=>Equals(block.Tag,"CodeBlock"))
                            .FontFamily.Source.Contains(localizedSettings.FontFamily.Source.Split(',')[0],StringComparison.Ordinal),
                        $"result body and code use the {tag} system font preferences");

                    TextBlock RenderLanguageSelection(UiLanguage value)
                    {
                        selector.SelectedValue=value;
                        var content=(FrameworkElement)localizedSettings.Content;
                        var size=new Size(804,641);
                        content.Measure(size); content.Arrange(new Rect(size)); content.UpdateLayout();
                        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(804,641,96,96,PixelFormats.Pbgra32);
                        bitmap.Render(content);
                        var selected=(ContentPresenter)selector.Template.FindName("ContentSite",selector);
                        return Descendants<TextBlock>(selected).Single();
                    }
                    var nativeJapanese=RenderLanguageSelection(UiLanguage.Japanese);
                    Check(nativeJapanese.Text=="日本語" && nativeJapanese.FontFamily.Source.StartsWith("Yu Gothic UI",StringComparison.Ordinal),
                        $"Japanese language option keeps its native font in {tag}");
                    var systemLabel=RenderLanguageSelection(UiLanguage.System);
                    Check(systemLabel.Text==UiText.T("SystemDefault") && systemLabel.FontFamily.Source==localizedSettings.FontFamily.Source,
                        $"Follow Windows selector uses the current {tag} UI font");
                    if(choice==UiLanguage.Japanese && Fonts.SystemFontFamilies.Any(font=>font.Source=="Yu Gothic UI")) {
                        var glyphs=GlyphRuns(VisualTreeHelper.GetDrawing(systemLabel)).ToArray();
                        Check(glyphs.Length>0 && glyphs.All(run=>run.GlyphTypeface.FamilyNames.Values.Contains("Yu Gothic UI")),
                            "Japanese Follow Windows selector actually renders Yu Gothic UI glyphs");
                    }
                    Capture(localizedSettings,$"settings-{tag}-general");
                    FindNamed<Button>(localizedSettings,"ToolbarPlacementNavigationButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Capture(localizedSettings,$"settings-{tag}-toolbar-position");
                    FindNamed<Button>(localizedSettings,"AiNavigationButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Capture(localizedSettings,$"settings-{tag}-ai");
                    Capture(languagePreview,$"result-{tag}");
                } finally { localizedSettings.Close(); }
            }
        } finally { languagePreview.Close(); UiText.Apply(UiLanguage.Chinese); }

        Settings? EditApiKey(Settings original,Action<PasswordBox> edit,bool save=true)
        {
            Settings? saved=null;
            var window=new SettingsWindow(original,next=>saved=next);
            var closed=false;
            window.Closed+=(_,_)=>closed=true;
            try {
                var input=FindNamed<PasswordBox>(window,"ApiKeyInput");
                edit(input);
                FindNamed<Button>(window,save?"SaveSettingsButton":"CancelSettingsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                return saved;
            } finally { if(!closed) window.Close(); }
        }
        var keySettings=new Settings();
        keySettings.SetKey("test-settings-key");
        var keptKey=EditApiKey(keySettings,input=>Check(input.Password=="test-settings-key" &&
            new PasswordBoxAutomationPeer(input).IsPassword(),"saved key is loaded into the masked password box"));
        Check(keptKey?.EncryptedKey==keySettings.EncryptedKey,"saving without editing retains the encrypted key");
        var clearedKey=EditApiKey(keySettings,input=>input.Clear());
        Check(clearedKey?.EncryptedKey=="","clearing the input and saving removes the key");
        var replacedKey=EditApiKey(keySettings,input=>input.Password="replacement-key");
        Check(replacedKey?.GetKey()=="replacement-key","editing the input and saving replaces the key");
        Check(EditApiKey(keySettings,input=>input.Clear(),save:false)==null && keySettings.GetKey()=="test-settings-key",
            "cancel discards key deletion");
        var unreadableKey=new Settings { EncryptedKey="invalid-base64" };
        var recoveredKey=EditApiKey(unreadableKey,input=> {
            Check(input.Password.Length==0 && AutomationProperties.GetHelpText(input)==UiText.T("ApiKeyUnavailable"),
                "an unreadable saved key leaves settings accessible and explains recovery");
            input.Password="recovered-key";
        });
        Check(recoveredKey?.GetKey()=="recovered-key","an unreadable key can be replaced");

        Settings? geminiSettings=null;
        var geminiWindow=new SettingsWindow(keySettings,next=>geminiSettings=next);
        var geminiWindowClosed=false;
        geminiWindow.Closed+=(_,_)=>geminiWindowClosed=true;
        try {
            FindNamed<Button>(geminiWindow,"AiNavigationButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var provider=FindNamed<ComboBox>(geminiWindow,"AiProviderSelector");
            var compatibleKey=FindNamed<PasswordBox>(geminiWindow,"ApiKeyInput");
            var geminiKey=FindNamed<PasswordBox>(geminiWindow,"GeminiApiKeyInput");
            var geminiModel=FindNamed<TextBox>(geminiWindow,"GeminiModelInput");
            var chatEndpoint=FindNamed<TextBox>(geminiWindow,"OpenAiEndpointInput");
            var geminiEndpoint=FindNamed<TextBox>(geminiWindow,"GeminiEndpointInput");
            var geminiHeader=FindNamed<TextBox>(geminiWindow,"GeminiKeyHeaderInput");
            provider.SelectedValue=AiProvider.Gemini;
            Check(provider.Items.Count==2 && ((StackPanel)geminiKey.Parent).Visibility==Visibility.Visible &&
                ((StackPanel)compatibleKey.Parent).Visibility==Visibility.Collapsed && geminiKey.Password=="",
                "choosing Gemini shows only its settings and does not reuse the compatible key");
            geminiKey.Password="gemini-settings-key";
            geminiModel.Text="gemini-flash-latest";
            geminiEndpoint.Text="https://example.com/ai/{model}:streamGenerateContentSse";
            geminiHeader.Text="x-company-key";
            provider.SelectedValue=AiProvider.ChatCompletions;
            Check(((StackPanel)compatibleKey.Parent).Visibility==Visibility.Visible && compatibleKey.Password=="test-settings-key",
                "switching back restores the compatible provider fields");
            chatEndpoint.Text="https://example.com/custom/openai";
            provider.SelectedValue=AiProvider.Gemini;
            Capture(geminiWindow,"settings-zh-CN-gemini");
            FindNamed<Button>(geminiWindow,"SaveSettingsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(geminiSettings is { AiProvider:AiProvider.Gemini,ActiveModel:"gemini-flash-latest",
                GeminiEndpoint:"https://example.com/ai/{model}:streamGenerateContentSse",GeminiKeyHeader:"x-company-key",
                OpenAiEndpoint:"https://example.com/custom/openai" } && geminiSettings.GetKey()=="gemini-settings-key" &&
                geminiSettings.EncryptedKey==keySettings.EncryptedKey,
                "settings save both configurable endpoints and the Google authentication header");
        } finally { if(!geminiWindowClosed) geminiWindow.Close(); }

        var selectionBox=new TextBox { Text="Hello selection" };
        var origin=new Window { Title="PopBar activation test source",Width=320,Height=160,Content=selectionBox };
        var foregroundResult=new Window { Title="PopBar activation test result",Width=320,Height=160,Content=new TextBlock { Text="Result preview" } };
        var activationToolbar=new ToolbarWindow();
        var oldCursor=System.Windows.Forms.Cursor.Position;
        void PumpRendering()
        {
            var frame=new System.Windows.Threading.DispatcherFrame();
            app.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle,new Action(()=>frame.Continue=false));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
        try {
            origin.Topmost=true; WindowPresentation.ShowForeground(origin); PumpRendering();
            var clickPoint=origin.PointToScreen(new Point(60,60));
            System.Windows.Forms.Cursor.Position=new System.Drawing.Point((int)clickPoint.X,(int)clickPoint.Y);
            mouse_event(2,0,0,0,0); mouse_event(4,0,0,0,0); PumpRendering();
            origin.Topmost=false;
            selectionBox.Focus(); selectionBox.Select(0,5);
            var originHandle=new System.Windows.Interop.WindowInteropHelper(origin).Handle;
            activationToolbar.Present(new Selection(originHandle,"Hello",true,null,null),new Native.Point(200,200),new Settings(),()=>{},_=>WindowPresentation.ShowForeground(foregroundResult),_=>{});
            PumpRendering();
            Check(Native.GetForegroundWindow()==originHandle,"toolbar presentation preserves source foreground");
            Descendants<Button>(activationToolbar).Single(item=>AutomationProperties.GetName(item)=="翻译").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpRendering();
            var resultHandle=new System.Windows.Interop.WindowInteropHelper(foregroundResult).Handle;
            Check(Native.GetForegroundWindow()==resultHandle && foregroundResult.IsActive,"translation click brings preview foreground");
            Check(!foregroundResult.Topmost && !activationToolbar.IsVisible && selectionBox.SelectedText=="Hello","preview activation preserves selection and normal z-order");
            foregroundResult.WindowState=WindowState.Minimized;
            WindowPresentation.ShowForeground(foregroundResult); PumpRendering();
            Check(foregroundResult.WindowState==WindowState.Normal && Native.GetForegroundWindow()==resultHandle,"foreground presentation restores minimized window");
            origin.Activate(); PumpRendering();
            Check(Native.GetForegroundWindow()==originHandle && !foregroundResult.Topmost,"preview does not keep stealing foreground");
        } finally { activationToolbar.Close(); foregroundResult.Close(); origin.Close(); System.Windows.Forms.Cursor.Position=oldCursor; }

        var chosenFont=Fonts.SystemFontFamilies.First(font=>font.Source=="Consolas");
        var sourceSettings=new Settings { ResultFontFamily=chosenFont.Source,ResultFontSize=18,AiSmartPrompt="Saved recognition rules" };
        sourceSettings.SetKey("test-settings-key");
        var settingsWindow=new SettingsWindow(sourceSettings,_=>{});
        try {
            var generalNav=FindNamed<Button>(settingsWindow,"GeneralNavigationButton");
            var aiNav=FindNamed<Button>(settingsWindow,"AiNavigationButton");
            var promptsNav=FindNamed<Button>(settingsWindow,"PromptsNavigationButton");
            var generalPage=FindNamed<FrameworkElement>(settingsWindow,"GeneralPage");
            var aiPage=FindNamed<FrameworkElement>(settingsWindow,"AiPage");
            var promptsPage=FindNamed<FrameworkElement>(settingsWindow,"PromptsPage");
            void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Click(aiNav);
            Check(aiPage.Visibility==Visibility.Visible && generalPage.Visibility!=Visibility.Visible && promptsPage.Visibility!=Visibility.Visible,"settings AI navigation");
            Click(promptsNav);
            Check(promptsPage.Visibility==Visibility.Visible && generalPage.Visibility!=Visibility.Visible && aiPage.Visibility!=Visibility.Visible,"settings prompts navigation");
            Click(generalNav);
            Check(generalPage.Visibility==Visibility.Visible && aiPage.Visibility!=Visibility.Visible && promptsPage.Visibility!=Visibility.Visible,"settings general navigation");
            var theme=FindNamed<ComboBox>(settingsWindow,"ThemeSelector");
            var toolbarOpacity=FindNamed<ComboBox>(settingsWindow,"ToolbarOpacitySelector");
            var position=FindNamed<ComboBox>(settingsWindow,"ToolbarPositionSelector");
            var resultFont=FindNamed<ComboBox>(settingsWindow,"ResultFontSelector");
            var resultSize=FindNamed<ComboBox>(settingsWindow,"ResultFontSizeSelector");
            var smartPage=FindNamed<FrameworkElement>(settingsWindow,"SmartButtonsPage");
            var smartNav=FindNamed<Button>(settingsWindow,"SmartButtonsNavigationButton");
            Check(smartNav.Parent is StackPanel sidebar && sidebar.Children[^1]==smartNav,"smart buttons settings are last in the sidebar");
            Check(smartPage.Visibility==Visibility.Collapsed,"smart buttons settings are separate from general settings");
            Click(smartNav);
            Check(smartPage.Visibility==Visibility.Visible && generalPage.Visibility==Visibility.Collapsed &&
                  aiPage.Visibility==Visibility.Collapsed && promptsPage.Visibility==Visibility.Collapsed,"smart buttons settings have their own navigation");
            var currencyTarget=FindNamed<ComboBox>(smartPage,"CurrencyTargetSelector");
            var currencySource=FindNamed<ComboBox>(smartPage,"CurrencySourceSelector");
            var currencyEnabled=FindNamed<CheckBox>(smartPage,"CurrencyConversionEnabled");
            var aiSmartAnalysis=FindNamed<CheckBox>(smartPage,"AiSmartAnalysisEnabled");
            var smartPrompt=FindNamed<TextBox>(smartPage,"AiSmartPromptInput");
            Check(smartPrompt.Text==sourceSettings.AiSmartPrompt,"smart analysis editor opens with the saved prompt");
            Check(currencyEnabled.IsChecked==true&&currencyTarget.Items.Cast<string>().SequenceEqual(new[]{"CNY","JPY","USD"}),"currency settings enabled with three target currencies");
            Capture(settingsWindow,"settings-smart-buttons");
            currencyTarget.SelectedItem="JPY"; currencySource.SelectedItem="USD";
            currencyEnabled.IsChecked=false; aiSmartAnalysis.IsChecked=false;
            smartPrompt.Text="Draft recognition rules";
            Click(generalNav); Click(smartNav);
            Check(Equals(currencyTarget.SelectedItem,"JPY") && Equals(currencySource.SelectedItem,"USD") &&
                  currencyEnabled.IsChecked==false && aiSmartAnalysis.IsChecked==false &&
                  sourceSettings.CurrencyTarget=="CNY" && sourceSettings.CurrencySource=="CNY" &&
                  sourceSettings.CurrencyConversion && sourceSettings.AiSmartAnalysis &&
                  smartPrompt.Text=="Draft recognition rules","smart buttons edits survive navigation as a draft");
            Click(FindNamed<Button>(smartPage,"RestoreAiSmartPromptButton"));
            Check(smartPrompt.Text.ReplaceLineEndings("\n")==AiSmartAnalysis.DefaultPrompt.ReplaceLineEndings("\n") &&
                  sourceSettings.AiSmartPrompt=="Saved recognition rules","restoring the default prompt edits only the draft");
            Click(generalNav);
            Check(Equals(resultFont.SelectedValue,chosenFont.Source) && Equals(resultSize.SelectedItem,18),"settings load saved result font and size");
            var placementPage=FindNamed<FrameworkElement>(settingsWindow,"ToolbarPlacementPage");
            Check(placementPage.Visibility==Visibility.Collapsed,"app placement list is separate from general settings");
            Click(FindNamed<Button>(settingsWindow,"ToolbarPlacementNavigationButton"));
            Check(placementPage.Visibility==Visibility.Visible && generalPage.Visibility==Visibility.Collapsed &&
                aiPage.Visibility==Visibility.Collapsed && promptsPage.Visibility==Visibility.Collapsed,"toolbar position page has its own navigation");
            Capture(settingsWindow,"settings-toolbar-position");
            Check(position.Items.Cast<ComboBoxItem>().Select(item=>item.Tag).SequenceEqual(new object[]{ToolbarPlacement.Above,ToolbarPlacement.Below}) && position.SelectedIndex==0,"toolbar placement settings options");
            position.SelectedIndex=1;
            Check(sourceSettings.ToolbarPosition==ToolbarPlacement.Above,"toolbar placement remains a draft until saved");
            var appPositionEditor=LogicalDescendants<AppToolbarPositionEditor>(settingsWindow).Single();
            var appPreset=FindNamed<ComboBox>(settingsWindow,"AppToolbarPresetSelector");
            var addApp=FindNamed<Button>(settingsWindow,"AppToolbarAddButton");
            Check(appPreset.Visibility==Visibility.Collapsed && appPositionEditor.ReadPositions().Count==10 &&
                appPositionEditor.ReadPositions().All(rule=>rule.Position==ToolbarPlacement.Below),"app placement presets are already enabled");
            var appPosition=FindNamed<ComboBox>(settingsWindow,"AppToolbarPositionSelector");
            appPosition.SelectedIndex=0;
            Check(appPositionEditor.ReadPositions()[0].Position==ToolbarPlacement.Above && sourceSettings.ToolbarAppPositions[0].Position==ToolbarPlacement.Below,
                "app positions can be changed without editing saved settings");
            Click(FindNamed<Button>(settingsWindow,"AppToolbarRemoveButton"));
            Check(appPositionEditor.ReadPositions().Count==9 && appPreset.Visibility==Visibility.Visible && appPreset.Items.Count==2,
                "removed preset becomes available to restore");
            appPreset.SelectedIndex=1;
            Click(addApp);
            Check(appPreset.Visibility==Visibility.Collapsed && appPositionEditor.ReadPositions()[^1].Executables.SequenceEqual(new[]{"ms-teams.exe","Teams.exe"}),
                "restoring Teams adds both executables without listing configured presets");
            var customApp=FindNamed<TextBox>(settingsWindow,"AppToolbarExecutableInput");
            customApp.Text=" Notepad ";
            Click(addApp);
            customApp.Text="NOTEPAD.EXE";
            Check(appPositionEditor.ReadPositions()[^1].Executables.Single()=="Notepad.exe" && !addApp.IsEnabled,
                "custom app accepts a bare process name and blocks case-insensitive duplicates");
            theme.ApplyTemplate();
            var toggle=(System.Windows.Controls.Primitives.ToggleButton)theme.Template.FindName("ToggleButton",theme);
            Check(Equals(toggle.Background,theme.Background) && Equals(toggle.BorderBrush,theme.BorderBrush),"theme selector surface and border bindings");
            Check(theme.Items.Cast<ComboBoxItem>().Select(item=>item.Tag).SequenceEqual(new object[]{AppTheme.System,AppTheme.Light,AppTheme.Dark}) && Equals((theme.SelectedItem as ComboBoxItem)?.Tag,AppTheme.System),"settings theme options");
            Check(toolbarOpacity.Items.Cast<ComboBoxItem>().Select(item=>item.Tag).SequenceEqual(Enumerable.Range(2,19).Select(step=>(object)(step*5))) &&
                Equals((toolbarOpacity.SelectedItem as ComboBoxItem)?.Tag,35) &&
                Equals((toolbarOpacity.SelectedItem as ComboBoxItem)?.Content,"35%"),
                "toolbar opacity settings options, percentage label and default");
            toolbarOpacity.SelectedItem=toolbarOpacity.Items.Cast<ComboBoxItem>().Single(item=>Equals(item.Tag,65));
            Check(sourceSettings.ToolbarIdleOpacity==35,"toolbar opacity remains a draft until saved");

            Click(promptsNav);
            Capture(settingsWindow,"settings-prompts");
            Click(generalNav); Capture(settingsWindow,"settings-general");
            Click(aiNav); Capture(settingsWindow,"settings-ai");
            Click(promptsNav);
            var list=FindNamed<ListBox>(settingsWindow,"PromptList");
            var name=FindNamed<TextBox>(settingsWindow,"PromptNameInput");
            var content=FindNamed<TextBox>(settingsWindow,"PromptContentInput");
            var add=FindNamed<Button>(settingsWindow,"AddPromptButton");
            var delete=FindNamed<Button>(settingsWindow,"DeletePromptButton");
            var empty=FindNamed<TextBlock>(settingsWindow,"PromptEmptyState");
            list.SelectedIndex=1;
            Check(name.Text=="解释" && content.Text==sourceSettings.Prompts[1].Prompt,"prompt selection loads draft");
            name.Text="草稿";
            var iconKind=FindNamed<ComboBox>(settingsWindow,"PromptIconKind");
            var iconInput=FindNamed<TextBox>(settingsWindow,"PromptIconInput");
            Check(iconKind.Items.Count==2 && iconKind.SelectedIndex==0,"icon editor offers emoji and Nerd Font only");
            iconKind.SelectedIndex=0; iconInput.Text="🌐";
            Check(((PromptAction)list.SelectedItem).Icon=="🌐" && sourceSettings.Prompts[1].Icon=="💡","icon editor uses isolated draft");
            list.SelectedIndex=0; list.SelectedIndex=1;
            Check(iconKind.SelectedIndex==0 && iconInput.Text=="🌐","icon selection survives switching prompts");
            Capture(settingsWindow,"settings-emoji");
            iconKind.SelectedIndex=1; iconInput.Text="U+F05CA";
            Capture(settingsWindow,"settings-nerd-compact");
            var fontOptions=FindNamed<Expander>(settingsWindow,"PromptIconAdvanced");
            fontOptions.IsExpanded=true;
            PumpFor(50);
            Capture(settingsWindow,"settings-nerd-font-options");
            fontOptions.IsExpanded=false;
            Check(sourceSettings.Prompts[1].Name=="解释" && sourceSettings.Prompts.Count==3,"prompt edits use isolated drafts");
            Click(add);
            Check(list.Items.Count==4 && list.SelectedIndex==3 && name.Text=="新操作","prompt add selects new draft");
            Click(delete);
            Check(list.Items.Count==3 && list.SelectedIndex==2,"prompt delete selects neighbor");
            while(list.Items.Count>0) Click(delete);
            Check(empty.Visibility==Visibility.Visible && add.IsEnabled && !delete.IsEnabled,"empty prompts state");
            Capture(settingsWindow,"settings-empty");
            for(var i=0;i<12;i++) Click(add);
            Check(list.Items.Count==12 && !add.IsEnabled && sourceSettings.Prompts.Count==3,"prompt twelve item limit");
            content.Text=string.Join("\n",Enumerable.Repeat("长提示词内容用于验证滚动和布局。",100));
            settingsWindow.Width=settingsWindow.MinWidth; settingsWindow.Height=settingsWindow.MinHeight;
            var root=(FrameworkElement)settingsWindow.Content;
            root.Measure(new Size(664,390)); root.Arrange(new Rect(0,0,664,390)); root.UpdateLayout();
            Check(content.ActualHeight==180 && content.ExtentHeight>content.ViewportHeight,"long prompt stays bounded and scrollable");
            var editorScroll=LogicalDescendants<ScrollViewer>(settingsWindow).Single(view=>view.Content is Grid);
            Check(editorScroll.ScrollableHeight>0,"minimum settings size scrolls prompt editor");
            Capture(settingsWindow,"settings-minimum-full");

            var firstDraft=(PromptAction)list.Items[0];
            list.SelectedItem=firstDraft;
            // The test uses real pointer input, so keep its window above other desktop apps.
            settingsWindow.Topmost=true;
            WindowPresentation.ShowForeground(settingsWindow);
            PumpFor(100);
            list.ScrollIntoView(firstDraft);
            PumpFor(100);
            var firstRow=(ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
            var viewport=Descendants<ScrollContentPresenter>(list).First();
            var start=firstRow.PointToScreen(new Point(30,firstRow.ActualHeight/2));
            var edge=viewport.PointToScreen(new Point(30,viewport.ActualHeight-3));
            var savedCursor=System.Windows.Forms.Cursor.Position;
            void MoveCursor(Point point) => System.Windows.Forms.Cursor.Position=new((int)point.X,(int)point.Y);
            var dragSteps=0;
            var markerSeen=false;
            var dragTimer=new System.Windows.Threading.DispatcherTimer { Interval=TimeSpan.FromMilliseconds(100) };
            dragTimer.Tick+=(_,_)=> {
                dragSteps++;
                if(dragSteps==1) MoveCursor(new Point(start.X+12,start.Y));
                if(dragSteps==2) MoveCursor(edge);
                if(dragSteps==17) {
                    markerSeen=System.Windows.Documents.AdornerLayer.GetAdornerLayer(viewport)?.GetAdorners(viewport)?.Length>0;
                    mouse_event(4,0,0,0,0);
                    dragTimer.Stop();
                }
            };
            try {
                MoveCursor(start);
                mouse_event(2,0,0,0,0);
                dragTimer.Start();
                PumpFor(2000);
                Check(markerSeen && ReferenceEquals(list.Items[11],firstDraft) && ReferenceEquals(list.SelectedItem,firstDraft),
                    "dragging a prompt to the lower edge scrolls, marks the insertion and reorders the draft");
                Check(sourceSettings.Prompts.Select(item=>item.Name).SequenceEqual(new[]{"翻译","解释","润色"}),
                    "dragging prompts leaves saved order alone until Save");
            } finally {
                dragTimer.Stop();
                mouse_event(4,0,0,0,0);
                System.Windows.Forms.Cursor.Position=savedCursor;
                settingsWindow.Topmost=false;
            }
        } finally { settingsWindow.Close(); }

        void VoidPump()
        {
            var frame=new System.Windows.Threading.DispatcherFrame();
            app.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle,new Action(()=>frame.Continue=false));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
        var scrollViewer=new MdXaml.MarkdownScrollViewer { Width=400,Height=200,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
        var scrollWindow=new Window { Width=400,Height=200,ShowActivated=false,WindowStartupLocation=WindowStartupLocation.Manual,Left=-10000,Top=-10000,Content=scrollViewer };
        try {
            scrollWindow.Show();
            scrollViewer.Markdown=string.Join("\n",Enumerable.Repeat("滚动测试行",200));
            ResultWindow.ScrollToEnd(scrollViewer);
            VoidPump();
            var innerScroll=Descendants<ScrollViewer>(scrollViewer).FirstOrDefault();
            Check(innerScroll is not null && innerScroll.ScrollableHeight>0 && Math.Abs(innerScroll.VerticalOffset-innerScroll.ScrollableHeight)<1,"markdown viewer scrolls to end");
        } finally { scrollWindow.Close(); }

        var resultWindow=new ResultWindow(new Settings { Model="test-model" },new PromptAction { Name="翻译",Prompt="Translate {{text}}" },new Selection(0,"hello",false,null,null));
        try {
            var output=FindNamed<MdXaml.MarkdownScrollViewer>(resultWindow,"OutputMarkdown");
            var status=FindNamed<TextBlock>(resultWindow,"StatusText");
            var stop=FindNamed<Button>(resultWindow,"StopButton");
            var followUpAction=FindNamed<Button>(resultWindow,"FollowUpButton");
            var copy=FindNamed<Button>(resultWindow,"CopyResultButton");
            var replace=FindNamed<Button>(resultWindow,"ReplaceButton");
            Check(!replace.IsEnabled && followUpAction.Visibility==Visibility.Collapsed &&
                FindNamed<Border>(resultWindow,"FollowUpComposer").Visibility==Visibility.Collapsed,
                "replacement and follow-up controls start hidden");
            Check(resultWindow.Title.Contains("翻译") && FindNamed<TextBlock>(resultWindow,"ResultModel").Text=="test-model" && status.Text.Length>0,"result title, model and status");
            Check(!output.IsToolBarVisible && output.IsSelectionEnabled && output.VerticalScrollBarVisibility==ScrollBarVisibility.Auto && output.HorizontalScrollBarVisibility==ScrollBarVisibility.Disabled,"result output is selectable scrolling markdown");
            Check(output.ClickAction==MdXaml.ClickAction.SafetyOpenBrowser,"result links use safe browser command");
            Check(ReferenceEquals(stop.Style,app.FindResource("DangerButtonStyle")) && ReferenceEquals(followUpAction.Style,app.FindResource("SubtleButtonStyle")) && ReferenceEquals(copy.Style,app.FindResource("SubtleButtonStyle")) && ReferenceEquals(replace.Style,app.FindResource("PrimaryButtonStyle")),"result action styles");
            resultWindow.Append("第一段");
            resultWindow.Append("第二段");
            Check(MarkdownText.Plain(output.Document!)=="第一段","streaming render is throttled");
            Check(resultWindow.PlainResult()=="第一段第二段","copy uses latest streamed content");
            resultWindow.RenderMarkdown("# 标题\n\n**粗体** 与 `代码`\n\n- 第一项\n- 第二项\n\n```\nvar x = 1;\nvar y = 2;\n```\n\n| 名称 | 含义 |\n| --- | --- |\n| SCM | 源代码管理 |");
            var rendered=output.Document!;
            Check(rendered.FontSize==13,"rendered markdown body font size");
            var renderedText=MarkdownText.Plain(rendered);
            Check(rendered.Blocks.OfType<System.Windows.Documents.Paragraph>().Any(block=>Equals(block.Tag,"Heading1")),"markdown heading rendered");
            Check(rendered.Blocks.OfType<System.Windows.Documents.List>().Any(),"markdown list rendered");
            Check(rendered.Blocks.OfType<System.Windows.Documents.Table>().Any(),"markdown table rendered");
            var codeBlock=rendered.Blocks.OfType<System.Windows.Documents.Paragraph>().FirstOrDefault(block=>Equals(block.Tag,"CodeBlock"));
            Check(codeBlock is not null,"markdown code block rendered");
            var inlineCode=rendered.Blocks.OfType<System.Windows.Documents.Paragraph>()
                .SelectMany(block=>block.Inlines).OfType<System.Windows.Documents.Run>().Single(run=>Equals(run.Tag,"CodeSpan"));
            Check(codeBlock!.FontFamily.Source.Contains("Cascadia") && inlineCode.FontFamily.Source.Contains("Cascadia"),"default markdown code uses app code font");
            var firstHeading=rendered.Blocks.OfType<System.Windows.Documents.Paragraph>().First(block=>Equals(block.Tag,"Heading1"));
            resultWindow.ApplyTypography(sourceSettings);
            Check(ReferenceEquals(rendered,output.Document) && rendered.FontFamily.Source==chosenFont.Source && rendered.FontSize==18
                && firstHeading.FontSize==24 && codeBlock.FontSize==17,"changing result typography updates the existing Markdown document");
            Check(codeBlock.FontFamily.Source==chosenFont.Source && inlineCode.FontFamily.Source==chosenFont.Source,
                "selected result font applies to code blocks and inline code");
            resultWindow.ApplyTypography(new Settings());
            Check(renderedText.Contains("标题") && renderedText.Contains("粗体") && renderedText.Contains("第一项") && renderedText.Contains("var x = 1;") && renderedText.Contains("var y = 2;") && renderedText.Contains("源代码管理") && !renderedText.Contains("**") && !renderedText.Contains("##"),"markdown rendering and plain text");
            resultWindow.RenderMarkdown("![追踪](file:///C:/PopBar-no-such-image.png)");
            Check(output.Document!.Blocks.OfType<System.Windows.Documents.Paragraph>().SelectMany(block=>block.Inlines).OfType<System.Windows.Documents.Hyperlink>().Any(),"markdown images become links");
            resultWindow.RenderMarkdown("```\n未闭合代码");
            Check(MarkdownText.Plain(output.Document!).Contains("未闭合代码"),"truncated markdown keeps content");
            resultWindow.RenderMarkdown(string.Join("\n",Enumerable.Repeat("- 项目",2000)));
            Check(MarkdownText.Plain(output.Document!).Contains("项目"),"large markdown renders");
            var hugeChunk="## 标题\n\n这是一段用于验证接近上限的长文本。\n\n- 项目一\n- 项目二\n\n";
            var huge=string.Concat(Enumerable.Repeat(hugeChunk,262144/hugeChunk.Length+1)).Substring(0,262144)+"\n\n```\nvar tail = 1;\n```\n\n结尾标记";
            var hugeStarted=System.Diagnostics.Stopwatch.StartNew();
            resultWindow.RenderMarkdown(huge);
            Check(hugeStarted.Elapsed<TimeSpan.FromSeconds(30) && MarkdownText.Plain(output.Document!).Contains("结尾标记"),"near-limit markdown renders within budget");
            resultWindow.RenderMarkdown("## 日语学习笔记\n\n这句话常用于商务邮件中，表示看过资料后整理了几个想确认的问题。\n\n「拝見する」是「見る」的自谦表达，语气礼貌。\n\n### 原文与读音\n\n> 資料を拝見し、気になる点をまとめました。\n> しりょうを はいけんし、きになる てんを まとめました。\n\n### 词语提示\n\n- 拝見（はいけん）：拜读、查看\n- 気になる（きになる）：在意、想确认");
            var finalDocument=output.Document!;
            var finalRoot=(FrameworkElement)resultWindow.Content;
            finalRoot.Measure(new Size(664,561)); finalRoot.Arrange(new Rect(0,0,664,561)); finalRoot.UpdateLayout();
            var finalHeading=finalDocument.Blocks.OfType<System.Windows.Documents.Paragraph>().First(block=>Equals(block.Tag,"Heading2"));
            var bodyParagraph=finalDocument.Blocks.OfType<System.Windows.Documents.Paragraph>().First(block=>block.Tag==null);
            Check(finalHeading.FontWeight==FontWeights.Bold && bodyParagraph.Margin.Bottom>=12,
                "markdown has bold headings and distinct paragraph spacing");
            ThemeManager.Apply(AppTheme.Light);
            var resultSurface=FindNamed<Border>(resultWindow,"ResultSurface");
            var resultLight=((SolidColorBrush)resultSurface.Background).Color;
            var outputLight=((SolidColorBrush)output.Background).Color;
            var headingLight=(finalHeading.Foreground as SolidColorBrush)?.Color;
            ThemeManager.Apply(AppTheme.Dark);
            Check(((SolidColorBrush)resultSurface.Background).Color!=resultLight,"result window updates theme");
            Check(((SolidColorBrush)output.Background).Color!=outputLight,"result markdown follows theme");
            var headingDark=(finalHeading.Foreground as SolidColorBrush)?.Color;
            Check(headingLight is not null && headingDark is not null && headingLight!=headingDark &&
                headingDark!=(bodyParagraph.Foreground as SolidColorBrush)?.Color,"markdown heading colors follow the theme and differ from body text");
            Capture(resultWindow,"result");
        } finally { resultWindow.Close(); }

        var conversationRequests=new List<IReadOnlyList<ChatMessage>>();
        var conversationSettings=new List<Settings>();
        var conversationResponses=new Queue<Func<Action<string>,Task>>();
        conversationResponses.Enqueue(append=> { append("初答"); return Task.CompletedTask; });
        conversationResponses.Enqueue(append=> { append("修正"); append("答"); return Task.CompletedTask; });
        conversationResponses.Enqueue(append=> { append("键盘回答"); return Task.CompletedTask; });
        conversationResponses.Enqueue(append=> { append("新设置回答"); return Task.CompletedTask; });
        var conversationWindow=new ResultWindow(new Settings { Model="test-model" },
            new PromptAction { Name="翻译",Prompt="Translate {{text}}" },new Selection(0,"hello",false,null,null),
            (requestSettings,messages,append,_,_)=> {
                conversationSettings.Add(requestSettings);
                conversationRequests.Add(messages.ToArray());
                return conversationResponses.Dequeue()(append);
            });
        try {
            await conversationWindow.Run();
            var followUp=FindNamed<TextBox>(conversationWindow,"FollowUpInput");
            var followUpAction=FindNamed<Button>(conversationWindow,"FollowUpButton");
            var composer=FindNamed<Border>(conversationWindow,"FollowUpComposer");
            var sendFollowUp=FindNamed<Button>(conversationWindow,"SendFollowUpButton");
            var conversationStatus=FindNamed<TextBlock>(conversationWindow,"StatusText");
            var conversationOutput=FindNamed<MdXaml.MarkdownScrollViewer>(conversationWindow,"OutputMarkdown");
            Check(followUp.AcceptsReturn && followUp.TextWrapping==TextWrapping.Wrap &&
                followUp.VerticalScrollBarVisibility==ScrollBarVisibility.Auto && !sendFollowUp.IsEnabled &&
                followUp.MinHeight==32 && followUp.VerticalContentAlignment==VerticalAlignment.Center,
                "follow-up composer is compact, multiline and vertically centered");
            Check(followUpAction.Visibility==Visibility.Visible && composer.Visibility==Visibility.Collapsed &&
                conversationStatus.Text.Length==0 &&
                LogicalDescendants<Button>(conversationWindow).All(button=>button.Name!="RetryButton"),
                "completed result hides follow-up input, status text and the old retry button");
            Capture(conversationWindow,"result-complete");
            followUpAction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(composer.Visibility==Visibility.Visible && followUpAction.Visibility==Visibility.Collapsed,
                "follow-up button reveals the composer and replaces itself");
            followUp.Text="第二点不对，请修正";
            Check(sendFollowUp.IsEnabled,"follow-up send enabled for non-empty input");
            await conversationWindow.SendFollowUp();
            Check(conversationRequests.Count==2 && conversationRequests[1].SequenceEqual(new[] {
                    new ChatMessage("user","Translate hello"),
                    new ChatMessage("assistant","初答"),
                    new ChatMessage("user","第二点不对，请修正") }),
                "follow-up submits the complete conversation snapshot");
            var conversationText=MarkdownText.Plain(conversationOutput.Document!);
            Check(conversationText.Contains("初答") && conversationText.Contains("第二点不对，请修正") &&
                conversationText.Contains("修正答") && conversationStatus.Text.Length==0,
                "result window shows users and both assistant turns without a completion label");
            Check(conversationWindow.PlainResult()=="修正答","copy uses only the latest assistant answer");
            followUp.Text="键盘追问";
            Check(!await conversationWindow.HandleFollowUpKeyAsync(System.Windows.Input.Key.Enter,System.Windows.Input.ModifierKeys.Shift) &&
                !await conversationWindow.HandleFollowUpKeyAsync(System.Windows.Input.Key.ImeProcessed,System.Windows.Input.ModifierKeys.None) &&
                conversationRequests.Count==2,"shift-enter and IME keys do not send follow-ups");
            Check(await conversationWindow.HandleFollowUpKeyAsync(System.Windows.Input.Key.Enter,System.Windows.Input.ModifierKeys.None) &&
                conversationRequests.Count==3 && followUp.Text.Length==0,"enter sends a non-empty follow-up");
            var updatedSettings=new Settings { Model="updated-model" };
            conversationWindow.ApplySettings(updatedSettings);
            followUp.Text="新设置追问";
            await conversationWindow.SendFollowUp();
            Check(ReferenceEquals(conversationSettings[^1],updatedSettings) &&
                LogicalDescendants<TextBlock>(conversationWindow).Any(text=>text.Text.Contains("updated-model")),
                "settings saved during a conversation apply to later follow-ups");
            Capture(conversationWindow,"result-followup");
        } finally { conversationWindow.Close(); }

        var partialRequests=new List<IReadOnlyList<ChatMessage>>();
        var partialResponses=new Queue<Func<Action<string>,Task>>();
        partialResponses.Enqueue(append=> { append("完整初答"); return Task.CompletedTask; });
        partialResponses.Enqueue(append=> { append("部分回答"); throw new InvalidDataException("模拟失败"); });
        partialResponses.Enqueue(append=> { append("补全回答"); return Task.CompletedTask; });
        partialResponses.Enqueue(_=> throw new InvalidDataException("空回答失败"));
        partialResponses.Enqueue(append=> { append("最终回答"); return Task.CompletedTask; });
        var partialWindow=new ResultWindow(new Settings { Model="test-model" },
            new PromptAction { Name="翻译",Prompt="Translate {{text}}" },new Selection(0,"hello",false,null,null),
            (_,messages,append,_,_)=> {
                partialRequests.Add(messages.ToArray());
                return partialResponses.Dequeue()(append);
            });
        try {
            await partialWindow.Run();
            FindNamed<Button>(partialWindow,"FollowUpButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var partialInput=FindNamed<TextBox>(partialWindow,"FollowUpInput");
            partialInput.Text="第一次追问";
            await partialWindow.SendFollowUp();
            Check(partialWindow.PlainResult()=="部分回答","failed streaming keeps its partial assistant answer");
            partialInput.Text="基于部分继续";
            await partialWindow.SendFollowUp();
            Check(partialRequests[2].SequenceEqual(new[] {
                    new ChatMessage("user","Translate hello"),
                    new ChatMessage("assistant","完整初答"),
                    new ChatMessage("user","第一次追问"),
                    new ChatMessage("assistant","部分回答"),
                    new ChatMessage("user","基于部分继续") }),
                "follow-up after a failure includes the visible partial answer");
            partialInput.Text="空回答后继续";
            await partialWindow.SendFollowUp();
            partialInput.Text="最终追问";
            await partialWindow.SendFollowUp();
            Check(partialRequests[4].Count(message=>message.Role=="assistant")==3 &&
                partialRequests[4].All(message=>message.Content.Length>0),
                "empty failed answers never enter conversation history");
        } finally { partialWindow.Close(); }

        var pendingGate=new TaskCompletionSource();
        var pendingResponses=new Queue<Func<Action<string>,Task>>();
        pendingResponses.Enqueue(append=> { append("初答"); return Task.CompletedTask; });
        pendingResponses.Enqueue(_=>pendingGate.Task);
        var pendingWindow=new ResultWindow(new Settings { Model="test-model" },
            new PromptAction { Name="翻译",Prompt="Translate {{text}}" },new Selection(0,"hello",false,null,null),
            (_,_,append,_,_)=>pendingResponses.Dequeue()(append));
        try {
            await pendingWindow.Run();
            FindNamed<Button>(pendingWindow,"FollowUpButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var pendingInput=FindNamed<TextBox>(pendingWindow,"FollowUpInput");
            var pendingSend=FindNamed<Button>(pendingWindow,"SendFollowUpButton");
            pendingInput.Text="等待中的追问";
            var pendingRequest=pendingWindow.SendFollowUp();
            Check(!pendingSend.IsEnabled,"follow-up send is disabled while a request is pending");
            pendingInput.Text="请求期间继续编辑";
            Check(pendingInput.IsEnabled && pendingInput.Text=="请求期间继续编辑",
                "follow-up draft stays editable during a request");
            pendingGate.SetResult();
            await pendingRequest;
            Check(pendingSend.IsEnabled,"follow-up send recovers after the request completes");
        } finally { pendingWindow.Close(); }

        var maxRequests=new List<IReadOnlyList<ChatMessage>>();
        var maxResponses=new Queue<Func<Action<string>,Task>>();
        maxResponses.Enqueue(append=> { append("x"); return Task.CompletedTask; });
        maxResponses.Enqueue(append=> { append("y"); return Task.CompletedTask; });
        var maxWindow=new ResultWindow(new Settings { Model="test-model" },
            new PromptAction { Prompt="{{text}}" },new Selection(0,new string('a',ResultWindow.MaxConversationCharacters-4),false,null,null),
            (_,messages,append,_,_)=> {
                maxRequests.Add(messages.ToArray());
                return maxResponses.Dequeue()(append);
            });
        try {
            await maxWindow.Run();
            FindNamed<Button>(maxWindow,"FollowUpButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var maxInput=FindNamed<TextBox>(maxWindow,"FollowUpInput");
            var maxStatus=FindNamed<TextBlock>(maxWindow,"StatusText");
            maxInput.Text="abc";
            await maxWindow.SendFollowUp();
            maxInput.Text="z";
            await maxWindow.SendFollowUp();
            Check(maxRequests.Count==2 && maxStatus.Text==UiText.T("ConversationTooLong") && maxInput.Text=="z",
                "conversation history accepts the limit and preserves over-limit drafts");
        } finally { maxWindow.Close(); }

        var focusTarget=new Window { Title="PopBar focus test",Width=300,Height=120 };
        var previewPrompt=new PromptAction { Name="预览" };
        var previewSelection=new Selection(0,"hello",false,null,null);
        var autoClosePreview=new ResultWindow(new Settings(),previewPrompt,previewSelection);
        var manualClosePreview=new ResultWindow(new Settings { AutoCloseResultOnDeactivate=false },previewPrompt,previewSelection);
        var autoClosed=false;
        autoClosePreview.Closed+=(_,_)=>autoClosed=true;
        try {
            WindowPresentation.ShowForeground(manualClosePreview);
            VoidPump();
            Check(manualClosePreview.IsActive,"manual close preview receives focus");
            manualClosePreview.RenderMarkdown("正文选区测试\n\n```\nvar answer = 42;\n```");
            manualClosePreview.UpdateLayout();
            var bodyViewer=Descendants<FlowDocumentScrollViewer>(manualClosePreview).Single();
            var bodyParagraph=bodyViewer.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().First();
            bodyViewer.Selection.Select(bodyParagraph.ContentStart,bodyParagraph.ContentEnd);
            var bodyPoint=bodyViewer.PointToScreen(new Point(40,25));
            var selectionPoint=new Native.Point((int)bodyPoint.X,(int)bodyPoint.Y);
            var bodySelection=manualClosePreview.ReadBodySelection(selectionPoint);
            var resultHandle=new System.Windows.Interop.WindowInteropHelper(manualClosePreview).Handle;
            Check(InputMonitor.CanCaptureWindow(resultHandle) && bodySelection is { Text:"正文选区测试",Editable:false } && bodySelection.Window==resultHandle,
                "result body selection is allowed and read-only");
            var footerPoint=FindNamed<Button>(manualClosePreview,"CopyResultButton").PointToScreen(new Point(5,5));
            Check(manualClosePreview.ReadBodySelection(new Native.Point((int)footerPoint.X,(int)footerPoint.Y))==null,
                "result footer ignores an existing body selection");
            bodyViewer.Focus();
            Check(manualClosePreview.ReadBodySelection(selectionPoint,true)?.Text=="正文选区测试","result body supports keyboard selection capture");
            FindNamed<Button>(manualClosePreview,"CopyResultButton").Focus();
            Check(manualClosePreview.ReadBodySelection(selectionPoint,true)==null,"result footer focus cannot capture stale body selection");
            var codeParagraph=bodyViewer.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Single(block=>Equals(block.Tag,"CodeBlock"));
            bodyViewer.Selection.Select(codeParagraph.ContentStart,codeParagraph.ContentEnd);
            Check(manualClosePreview.ReadBodySelection(selectionPoint)?.Text.Contains("var answer = 42;")==true,"result code selection is readable");
            bodyViewer.Selection.Select(bodyParagraph.ContentStart,bodyParagraph.ContentStart);
            Check(manualClosePreview.ReadBodySelection(selectionPoint)==null,"empty result selection is ignored");
            var ignoredSettings=new SettingsWindow(new Settings(),_=>{});
            var ignoredToolbar=new ToolbarWindow();
            try {
                Check(!InputMonitor.CanCaptureWindow(new System.Windows.Interop.WindowInteropHelper(ignoredSettings).EnsureHandle()) &&
                    !InputMonitor.CanCaptureWindow(new System.Windows.Interop.WindowInteropHelper(ignoredToolbar).EnsureHandle()),
                    "settings and toolbar windows remain excluded");
            } finally { ignoredSettings.Close(); ignoredToolbar.Close(); }
            WindowPresentation.ShowForeground(autoClosePreview);
            VoidPump();
            Check(autoClosePreview.IsActive && !manualClosePreview.IsActive,"auto-close preview receives focus");
            WindowPresentation.ShowForeground(focusTarget);
            PumpFor(300);
            Check(!autoClosePreview.IsActive && !autoClosed,"losing focus does not close the preview immediately");

            autoClosePreview.Activate();
            VoidPump();
            Check(autoClosePreview.IsActive,"preview regains focus before its countdown expires");
            PumpFor(5200);
            Check(!autoClosed,"refocusing cancels the auto-close countdown");
            Check(manualClosePreview.IsVisible,"disabled auto-close keeps the inactive preview open");

            focusTarget.Activate();
            PumpFor(5200);
            Check(autoClosed,"preview closes after five seconds continuously out of focus");
        } finally {
            if(!autoClosed) autoClosePreview.Close();
            manualClosePreview.Close();
            focusTarget.Close();
        }

        var closeResponses=new Queue<Func<Action<string>,Task>>();
        closeResponses.Enqueue(append=> { append("初答"); return Task.CompletedTask; });
        var conversationClose=new ResultWindow(new Settings { AutoCloseResultOnDeactivate=false },previewPrompt,previewSelection,
            (_,_,append,_,_)=>closeResponses.Dequeue()(append));
        var conversationFocus=new Window { Title="PopBar conversation focus test",Width=300,Height=120 };
        var conversationClosed=false;
        conversationClose.Closed+=(_,_)=>conversationClosed=true;
        try {
            WindowPresentation.ShowForeground(conversationClose);
            VoidPump();
            FindNamed<Button>(conversationClose,"FollowUpButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(FindNamed<Border>(conversationClose,"FollowUpComposer").Visibility==Visibility.Visible,
                "follow-up mode reveals the composer before anything is sent");
            var closeInput=FindNamed<TextBox>(conversationClose,"FollowUpInput");
            conversationClose.UpdateLayout();
            var caret=closeInput.GetRectFromCharacterIndex(0);
            Check(closeInput.IsKeyboardFocusWithin &&
                Math.Abs((caret.Top+caret.Bottom)/2-closeInput.ActualHeight/2)<=4,
                "empty follow-up caret is vertically centered in a compact input");
            WindowPresentation.ShowForeground(conversationFocus);
            PumpFor(5200);
            Check(!conversationClosed && conversationClose.IsVisible,
                "entering follow-up mode disables auto-close before sending");
            WindowPresentation.ShowForeground(conversationClose);
            VoidPump();
            conversationClose.SetAutoCloseOnDeactivate(true);
            WindowPresentation.ShowForeground(conversationFocus);
            PumpFor(5200);
            Check(!conversationClosed && conversationClose.IsVisible,
                "re-enabling auto-close does not affect follow-up mode");
        } finally {
            if(!conversationClosed) conversationClose.Close();
            conversationFocus.Close();
        }

        app.Shutdown();
        return passed;
    }

    static IEnumerable<GlyphRun> GlyphRuns(Drawing? drawing)
    {
        if(drawing is GlyphRunDrawing glyph) yield return glyph.GlyphRun;
        if(drawing is DrawingGroup group)
            foreach(var child in group.Children)
                foreach(var run in GlyphRuns(child)) yield return run;
    }

    // Optional off-screen visual artifacts; never starts requests or writes user settings.
    static void Capture(Window window,string name)
    {
        var folder=Environment.GetEnvironmentVariable("POPBAR_UI_ARTIFACTS");
        if(string.IsNullOrEmpty(folder)) return;
        System.IO.Directory.CreateDirectory(folder);
        foreach(var theme in new[]{AppTheme.Light,AppTheme.Dark}) {
            ThemeManager.Apply(theme);
            var root=(FrameworkElement)window.Content;
            var size=window switch {
                ToolbarWindow=>new Size(window.ActualWidth,window.ActualHeight),
                ResultPopupWindow=>new Size(window.Width,window.Height),
                _=>new Size(window.Width-16,window.Height-39)
            };
            root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
            foreach(var scale in new[]{1.0,1.5}) {
                var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(size.Width*scale),(int)Math.Ceiling(size.Height*scale),96*scale,96*scale,PixelFormats.Pbgra32);
                var background=new DrawingVisual();
                using(var drawing=background.RenderOpen()) drawing.DrawRectangle(window.Background,null,new Rect(size));
                bitmap.Render(background);
                bitmap.Render(root);
                var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var file=System.IO.File.Create(System.IO.Path.Combine(folder,$"{name}-{theme}-{scale*100:0}.png"));
                encoder.Save(file);
            }
        }
    }

    internal static IEnumerable<T> LogicalDescendants<T>(DependencyObject root) where T:DependencyObject
    {
        if(root is T match) yield return match;
        foreach(var child in LogicalTreeHelper.GetChildren(root)) {
            if(child is not DependencyObject dependency) continue;
            foreach(var descendant in LogicalDescendants<T>(dependency)) yield return descendant;
        }
    }

    internal static T FindNamed<T>(DependencyObject root,string name) where T:DependencyObject
    {
        if(root is FrameworkElement element && element.Name==name && root is T match) return match;
        foreach(var child in LogicalTreeHelper.GetChildren(root)) {
            if(child is not DependencyObject dependency) continue;
            try { return FindNamed<T>(dependency,name); } catch(InvalidOperationException) { }
        }
        throw new InvalidOperationException($"Named control not found: {name}");
    }

    internal static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {
        if(root is T match) yield return match;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var child in Descendants<T>(VisualTreeHelper.GetChild(root,i))) yield return child;
    }
}
