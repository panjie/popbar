using PopBar;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

internal static class UiChecks
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool PrintWindow(nint window,nint dc,uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern void mouse_event(uint flags,uint x,uint y,uint data,nuint extra);
    public static int Run()
    {
        var passed=0;
        Exception? error=null;
        var thread=new Thread(()=> { try { passed=RunOnUiThread(); } catch(Exception ex) { error=ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if(error!=null) throw new Exception("UI checks failed",error);
        return passed;
    }

    static int RunOnUiThread()
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
        Check(missing.Text=="\uE774" && missingWarning.Length>0,"missing Nerd Font falls back safely");
        var restored=System.Text.Json.JsonSerializer.Deserialize<PromptAction>(System.Text.Json.JsonSerializer.Serialize(emojiPrompt.Copy()))!;
        Check(restored.IconKind==PromptIconKind.Emoji && restored.Icon==emojiPrompt.Icon,"icon draft copy and JSON round trip");
        Check(System.Text.Json.JsonSerializer.Deserialize<PromptAction>("{}")!.IconKind==PromptIconKind.Default,"legacy prompts retain default icons");
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
            var toolbarSurface=(Border)toolbar.Content;
            Check(toolbarSurface.Name=="ToolbarSurface" && Math.Abs(toolbarSurface.Opacity-ToolbarWindow.IdleOpacity)<0.001,"toolbar starts semi-transparent");
            var panel=Descendants<WrapPanel>(toolbar).Single();
            var buttons=panel.Children.OfType<Button>().ToList();
            var names=new[] { "复制","搜索","翻译","解释","润色","设置" };
            Check(buttons.Count==names.Length && buttons.All(button=>button.Content is TextBlock),"toolbar uses icon-only buttons");
            Check(buttons.Select(button=>button.ToolTip as string).SequenceEqual(names) && buttons.Select(AutomationProperties.GetName).SequenceEqual(names),"toolbar button names and order");
            Check(buttons.All(button=>!button.Focusable) && !toolbar.ShowActivated,"toolbar does not take focus");
            Check(panel.Children.OfType<Border>().Any(border=>border.Width==1),"toolbar visual group separators");
            Check(!buttons.Any(button=>new[]{"剪切","粘贴","替换原文"}.Contains(button.ToolTip as string)),"read-only selection hides edit actions");
            Capture(toolbar,"toolbar");
            toolbarSurface.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0) { RoutedEvent=System.Windows.Input.Mouse.MouseEnterEvent });
            PumpFor(250);
            Check(Math.Abs(toolbarSurface.Opacity-1)<0.001,"toolbar hover becomes opaque");
            toolbar.Present(new Selection(0,"hello",false,null,null),new Native.Point { X=100,Y=100 },new Settings { Prompts=new(){emojiPrompt} },()=>{},_=>{},_=>{});
            Check(!toolbarSurface.HasAnimatedProperties && Math.Abs(toolbarSurface.Opacity-ToolbarWindow.IdleOpacity)<0.001,"toolbar presentation resets idle opacity");
            var custom=Descendants<Button>(toolbar).Single(item=>AutomationProperties.GetName(item)=="自定义");
            Check(custom.Content is Viewbox box && box.Child is TextBlock text && text.Text=="👩‍💻" && !custom.Focusable,"toolbar renders custom emoji without focus");
            toolbarSurface.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0) { RoutedEvent=System.Windows.Input.Mouse.MouseEnterEvent });
            PumpFor(250);
            Check(Math.Abs(toolbarSurface.Opacity-1)<0.001,"toolbar hover becomes opaque after present");
            toolbarSurface.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0) { RoutedEvent=System.Windows.Input.Mouse.MouseLeaveEvent });
            PumpFor(250);
            Check(Math.Abs(toolbarSurface.Opacity-ToolbarWindow.IdleOpacity)<0.001,"toolbar leave returns to idle opacity");
            Capture(toolbar,"toolbar-emoji");
        } finally { toolbar.Close(); System.Windows.Forms.Cursor.Position=toolbarCursor; }

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
        var sourceSettings=new Settings { ResultFontFamily=chosenFont.Source,ResultFontSize=18 };
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
            var keyInput=FindNamed<PasswordBox>(settingsWindow,"ApiKeyInput");
            var keyMask=FindNamed<TextBlock>(settingsWindow,"SavedApiKeyMask");
            var clearKey=FindNamed<CheckBox>(settingsWindow,"ClearSavedApiKey");
            Check(keyMask.Visibility==Visibility.Visible && keyInput.Password.Length==0,"saved key displays mask without populating password");
            keyInput.Password="replacement";
            Check(keyMask.Visibility==Visibility.Collapsed,"replacement key hides saved mask");
            keyInput.Clear();
            Check(keyMask.Visibility==Visibility.Visible,"empty replacement retains saved key indicator");
            clearKey.IsChecked=true;
            Check(keyMask.Visibility==Visibility.Collapsed,"clearing saved key hides mask");
            clearKey.IsChecked=false;
            Check(keyMask.Visibility==Visibility.Visible && sourceSettings.GetKey()=="test-settings-key","cancel clearing restores mask without changing stored key");
            Check(aiPage.Visibility==Visibility.Visible && generalPage.Visibility!=Visibility.Visible && promptsPage.Visibility!=Visibility.Visible,"settings AI navigation");
            Click(promptsNav);
            Check(promptsPage.Visibility==Visibility.Visible && generalPage.Visibility!=Visibility.Visible && aiPage.Visibility!=Visibility.Visible,"settings prompts navigation");
            Click(generalNav);
            Check(generalPage.Visibility==Visibility.Visible && aiPage.Visibility!=Visibility.Visible && promptsPage.Visibility!=Visibility.Visible,"settings general navigation");
            var theme=FindNamed<ComboBox>(settingsWindow,"ThemeSelector");
            var position=FindNamed<ComboBox>(settingsWindow,"ToolbarPositionSelector");
            var resultFont=FindNamed<ComboBox>(settingsWindow,"ResultFontSelector");
            var resultSize=FindNamed<ComboBox>(settingsWindow,"ResultFontSizeSelector");
            Check(Equals(resultFont.SelectedValue,chosenFont.Source) && Equals(resultSize.SelectedItem,18),"settings load saved result font and size");
            Check(position.Items.Cast<ComboBoxItem>().Select(item=>item.Tag).SequenceEqual(new object[]{ToolbarPlacement.Above,ToolbarPlacement.Below}) && position.SelectedIndex==0,"toolbar placement settings options");
            position.SelectedIndex=1;
            Check(sourceSettings.ToolbarPosition==ToolbarPlacement.Above,"toolbar placement remains a draft until saved");
            theme.ApplyTemplate();
            var toggle=(System.Windows.Controls.Primitives.ToggleButton)theme.Template.FindName("ToggleButton",theme);
            Check(Equals(toggle.Background,theme.Background) && Equals(toggle.BorderBrush,theme.BorderBrush),"theme selector surface and border bindings");
            Check(theme.Items.Cast<ComboBoxItem>().Select(item=>item.Tag).SequenceEqual(new object[]{AppTheme.System,AppTheme.Light,AppTheme.Dark}) && Equals((theme.SelectedItem as ComboBoxItem)?.Tag,AppTheme.System),"settings theme options");

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
            iconKind.SelectedIndex=1; iconInput.Text="🌐";
            Check(((PromptAction)list.SelectedItem).Icon=="🌐" && sourceSettings.Prompts[1].Icon=="","icon editor uses isolated draft");
            list.SelectedIndex=0; list.SelectedIndex=1;
            Check(iconKind.SelectedIndex==1 && iconInput.Text=="🌐","icon selection survives switching prompts");
            Capture(settingsWindow,"settings-emoji");
            iconKind.SelectedIndex=2; iconInput.Text="U+F05CA";
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

            WindowPresentation.ShowForeground(settingsWindow);
            var firstDraft=(PromptAction)list.Items[0];
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
            var retry=FindNamed<Button>(resultWindow,"RetryButton");
            var copy=FindNamed<Button>(resultWindow,"CopyResultButton");
            var replace=FindNamed<Button>(resultWindow,"ReplaceButton");
            Check(!replace.IsEnabled,"replacement disabled before a completed request");
            Check(resultWindow.Title.Contains("翻译") && LogicalDescendants<TextBlock>(resultWindow).Any(text=>text.Text.Contains("test-model") && text.Text.Contains("5")) && status.Text.Length>0,"result title, metadata and status");
            Check(!output.IsToolBarVisible && output.IsSelectionEnabled && output.VerticalScrollBarVisibility==ScrollBarVisibility.Auto && output.HorizontalScrollBarVisibility==ScrollBarVisibility.Disabled,"result output is selectable scrolling markdown");
            Check(output.ClickAction==MdXaml.ClickAction.SafetyOpenBrowser,"result links use safe browser command");
            Check(ReferenceEquals(stop.Style,app.FindResource("DangerButtonStyle")) && ReferenceEquals(retry.Style,app.FindResource("SubtleButtonStyle")) && ReferenceEquals(copy.Style,app.FindResource("SubtleButtonStyle")) && ReferenceEquals(replace.Style,app.FindResource("PrimaryButtonStyle")),"result action styles");
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
            resultWindow.RenderMarkdown("## 读音（假名）\n\n> じしんの りかいが なかなか おいつかない ぶぶんが おおく、しりょうを はいけんし きになる てんを まとめました。\n\n**难读词提示**\n\n- 自身：じしん\n- 追い付かない：おいつかない\n- 拝見し：はいけんし\n\n---\n\n## 例句\n\n短い例文を `メモ` に残します。");
            var finalDocument=output.Document!;
            var finalRoot=(FrameworkElement)resultWindow.Content;
            finalRoot.Measure(new Size(664,561)); finalRoot.Arrange(new Rect(0,0,664,561)); finalRoot.UpdateLayout();
            var finalHeading=finalDocument.Blocks.OfType<System.Windows.Documents.Paragraph>().First(block=>Equals(block.Tag,"Heading2"));
            ThemeManager.Apply(AppTheme.Light);
            var resultLight=((SolidColorBrush)resultWindow.Background).Color;
            var outputLight=((SolidColorBrush)output.Background).Color;
            var headingLight=(finalHeading.Foreground as SolidColorBrush)?.Color;
            ThemeManager.Apply(AppTheme.Dark);
            Check(((SolidColorBrush)resultWindow.Background).Color!=resultLight,"result window updates theme");
            Check(((SolidColorBrush)output.Background).Color!=outputLight,"result markdown follows theme");
            Check(headingLight is not null && (finalHeading.Foreground as SolidColorBrush)?.Color!=headingLight,"markdown headings follow theme");
            Capture(resultWindow,"result");
        } finally { resultWindow.Close(); }

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
        app.Shutdown();
        return passed;
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
            var size=window is ToolbarWindow?new Size(window.ActualWidth,window.ActualHeight):new Size(window.Width-16,window.Height-39);
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
