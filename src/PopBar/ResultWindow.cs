using ICSharpCode.AvalonEdit;
using MdXaml;
using System.Text;
using System.Windows.Documents;
using System.Windows.Threading;

namespace PopBar;

internal sealed class ResultWindow:Window
{
    readonly MarkdownScrollViewer output=new();
    readonly StringBuilder markdown=new();
    readonly TextBlock status=new() { FontSize=12,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center };
    readonly TextBlock metadata;
    readonly Button retry,stop,replace,copy;
    string? statusKey="PreparingRequest";
    string? preferredFont;
    readonly DispatcherTimer autoCloseTimer=new() { Interval=TimeSpan.FromSeconds(5) };
    bool autoCloseOnDeactivate;
    CancellationTokenSource? pending;
    readonly Settings settings; readonly PromptAction prompt; readonly Selection selection;
    long nextRender;
    int renderedLength;
    bool closed;
    internal ResultWindow(Settings settings,PromptAction prompt,Selection selection)
    {
        this.settings=settings; this.prompt=prompt; this.selection=selection;
        autoCloseOnDeactivate=settings.AutoCloseResultOnDeactivate;
        Width=560; Height=440; MinWidth=360; MinHeight=240;
        WindowStyle=WindowStyle.ToolWindow;
        ShowInTaskbar=false;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        Ui.Style(this);
        var root=new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        Content=root;

        metadata=new TextBlock {
            FontSize=11,
            Margin=new Thickness(16,8,16,3),TextTrimming=TextTrimming.CharacterEllipsis
        };
        metadata.SetResourceReference(TextBlock.ForegroundProperty,"MutedTextBrush");
        root.Children.Add(metadata);

        output.Name="OutputMarkdown"; output.IsToolBarVisible=false; output.IsSelectionEnabled=true;
        output.VerticalScrollBarVisibility=ScrollBarVisibility.Auto; output.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;
        output.ClickAction=ClickAction.SafetyOpenBrowser;
        output.MarkdownStyle=(Style)FindResource("ResultMarkdownStyle");
        ApplyTypography(settings);
        output.SetResourceReference(Control.ForegroundProperty,"TextBrush");
        output.SetResourceReference(Control.BackgroundProperty,"WindowBackgroundBrush");
        Grid.SetRow(output,1); root.Children.Add(output);

        status.Name="StatusText";
        status.Margin=new Thickness(0,0,8,0);
        status.SetResourceReference(TextBlock.ForegroundProperty,"MutedTextBrush");
        status.SetBinding(FrameworkElement.ToolTipProperty,new System.Windows.Data.Binding(nameof(TextBlock.Text)) { Source=status });
        var footer=new Border { BorderThickness=new Thickness(0,1,0,0),Padding=new Thickness(16,5,10,5) };
        Grid.SetRow(footer,2); root.Children.Add(footer);
        footer.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");
        var footerContent=new DockPanel(); footer.Child=footerContent;
        var buttons=new StackPanel { Orientation=Orientation.Horizontal };
        DockPanel.SetDock(buttons,Dock.Right); footerContent.Children.Add(buttons);
        footerContent.Children.Add(status);

        Button ActionButton(string name,string label,Action action,string style="SubtleButtonStyle")
        {
            var button=Ui.Button(label,action,style);
            button.Name=name;
            button.MinHeight=28;
            button.Padding=new Thickness(9,3,9,3);
            button.Margin=new Thickness(2,0,0,0);
            buttons.Children.Add(button);
            return button;
        }
        stop=ActionButton("StopButton","",()=>pending?.Cancel(),"DangerButtonStyle");
        retry=ActionButton("RetryButton","",()=>_=Run());
        copy=ActionButton("CopyResultButton","",()=> {
            SetStatus(Actions.Copy(PlainResult())?"Copied":"ClipboardBusy");
        });
        replace=ActionButton("ReplaceButton","",async ()=> {
            replace!.IsEnabled=false;
            try {
                var replaced=await Actions.Replace(selection,PlainResult());
                if(closed) return;
                if(replaced) { Close(); return; }
                SetStatus("CannotVerifySelection");
            }
            catch { SetStatus("ReplaceFailed"); }
        },"PrimaryButtonStyle");
        replace.IsEnabled=false;
        replace.Visibility=stop.Visibility=Visibility.Collapsed;
        RefreshLanguage();

        Activated+=(_,_)=>autoCloseTimer.Stop();
        Deactivated+=(_,_)=> { if(autoCloseOnDeactivate && !closed) autoCloseTimer.Start(); };
        autoCloseTimer.Tick+=(_,_)=> {
            autoCloseTimer.Stop();
            if(autoCloseOnDeactivate && !IsActive && !closed) Close();
        };
        Loaded+=async (_,_)=>await Run();
        Closed+=(_,_)=> {
            closed=true;
            autoCloseTimer.Stop();
            pending?.Cancel();
        };
        PreviewKeyDown+=(_,e)=> { if(e.Key==System.Windows.Input.Key.Escape) Close(); };
    }
    internal void RefreshLanguage()
    {
        BuiltInPrompts.LocalizeName(prompt);
        Ui.ApplyTextLanguage(this);
        ApplyResultFont();
        Title=$"PopBar · {prompt.Name}";
        var selected=T("SelectedCharacterCount",selection.Text.Length);
        metadata.Text=string.IsNullOrWhiteSpace(settings.Model)?selected:$"{settings.Model}  ·  {selected}";
        stop.Content=T("Stop");
        retry.Content=T("Retry");
        copy.Content=T("Copy");
        replace.Content=T("ReplaceOriginal");
        if(statusKey!=null) status.Text=T(statusKey);
    }
    // Read our own WPF selection directly; only the result body is eligible.
    internal Selection? ReadBodySelection(Native.Point point,bool focused=false)
    {
        if(closed || !IsVisible) return null;
        var viewer=FindBodyViewer(output);
        if(viewer==null) return null;
        if(focused) {
            if(!viewer.IsKeyboardFocusWithin) return null;
        } else {
            var local=viewer.PointFromScreen(new Point(point.X,point.Y));
            if(!new Rect(viewer.RenderSize).Contains(local)) return null;
            var hit=viewer.InputHitTest(local) as DependencyObject;
            for(var current=hit;current!=null && current!=viewer;) {
                if(current is System.Windows.Controls.Primitives.ScrollBar) return null;
                current=current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
            }
        }
        var text=viewer.Selection?.Text;
        if(string.IsNullOrWhiteSpace(text) || text.Length>SelectionReader.MaxText) return null;
        return new(new System.Windows.Interop.WindowInteropHelper(this).Handle,text,false,null,null);
    }
    static FlowDocumentScrollViewer? FindBodyViewer(DependencyObject root)
    {
        if(root is FlowDocumentScrollViewer viewer) return viewer;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            if(FindBodyViewer(VisualTreeHelper.GetChild(root,i)) is { } found) return found;
        return null;
    }
    void SetStatus(string key)
    {
        statusKey=key;
        status.Text=T(key);
    }
    internal void ApplyTypography(Settings preferences)
    {
        preferredFont=preferences.ResultFontFamily;
        ApplyResultFont();
        var size=ResultTypography.NormalizeSize(preferences.ResultFontSize);
        output.FontSize=size;
        output.Resources["ResultBodySize"]=(double)size;
        output.Resources["ResultLineHeight"]=Math.Round(size*20.0/ResultTypography.DefaultSize);
        output.Resources["ResultHeading1Size"]=(double)(size+6);
        output.Resources["ResultHeading2Size"]=(double)(size+3);
        output.Resources["ResultHeading3Size"]=(double)(size+1);
        output.Resources["ResultCodeSize"]=(double)(size-1);
    }
    void ApplyResultFont()
    {
        var font=ResultTypography.ResolveFont(preferredFont);
        output.FontFamily=font;
        output.Resources["ResultBodyFont"]=font;
        output.Resources["ResultCodeFont"]=ReferenceEquals(font,ResultTypography.DefaultFont)?ResultTypography.DefaultCodeFont:font;
    }
    internal void SetAutoCloseOnDeactivate(bool enabled)
    {
        if(autoCloseOnDeactivate==enabled || closed) return;
        autoCloseOnDeactivate=enabled;
        if(enabled && IsVisible && !IsActive) autoCloseTimer.Start();
        else autoCloseTimer.Stop();
    }
    async Task Run()
    {
        if(pending!=null || closed) return;
        using var request=new CancellationTokenSource();
        pending=request;
        markdown.Clear(); renderedLength=0; nextRender=0; Display("");
        retry.Visibility=replace.Visibility=Visibility.Collapsed;
        stop.Visibility=Visibility.Visible;
        SetStatus("WaitingForResponse");
        try {
            await AiClient.Stream(settings,prompt,selection.Text,chunk=> {
                if(closed) return;
                if(markdown.Length==0) SetStatus("StreamingResult");
                Append(chunk);
            },request.Token,onThinking:()=> {
                if(!closed) SetStatus("ModelThinking");
            });
            if(!closed) {
                Render(true);
                SetStatus("RequestComplete");
                replace.IsEnabled=selection.Editable;
                replace.Visibility=selection.Editable?Visibility.Visible:Visibility.Collapsed;
            }
        } catch(OperationCanceledException) { if(!closed) { Render(true); SetStatus("RequestStopped"); } }
        catch(Exception ex) {
            if(!closed) {
                Render(true);
                if(ex is InvalidOperationException or InvalidDataException) {
                    statusKey=null;
                    status.Text=ex.Message;
                } else SetStatus("RequestFailed");
            }
        }
        finally {
            pending=null;
            if(!closed) {
                retry.Visibility=Visibility.Visible;
                stop.Visibility=Visibility.Collapsed;
            }
        }
    }
    internal void Append(string chunk)
    {
        markdown.Append(chunk); Render();
    }
    internal string PlainResult()
    {
        if(!closed && markdown.Length!=renderedLength) Display(markdown.ToString());
        return MarkdownText.Plain(output.Document);
    }
    void Render(bool force=false)
    {
        if(closed) return;
        if(!force && Environment.TickCount64<nextRender) return;
        var source=markdown.ToString();
        if(source.Length==renderedLength) return;
        var started=Environment.TickCount64;
        Display(source);
        var elapsed=Environment.TickCount64-started;
        nextRender=Environment.TickCount64+Math.Max(120,elapsed*8);
    }
    internal void RenderMarkdown(string source)
    {
        markdown.Clear(); markdown.Append(source); nextRender=0;
        Display(source);
    }
    void Display(string source)
    {
        var scroll=FindScrollViewer(output);
        var offset=scroll?.VerticalOffset??0;
        var followEnd=scroll==null || scroll.ScrollableHeight-offset<8;
        var safe=MarkdownText.DemoteImages(source);
        try { output.Markdown=safe; NormalizeCodeBlocks(output.Document!); }
        catch {
            try {
                var fallback=new FlowDocument(new Paragraph(new Run(safe))) { Style=output.MarkdownStyle };
                output.Document=fallback;
            } catch { }
        }
        renderedLength=source.Length;
        if(followEnd) ScrollToEnd(output);
        else scroll?.ScrollToVerticalOffset(offset);
    }
    internal static void ScrollToEnd(MarkdownScrollViewer viewer)
    {
        viewer.UpdateLayout();
        FindScrollViewer(viewer)?.ScrollToEnd();
        viewer.Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>FindScrollViewer(viewer)?.ScrollToEnd()));
    }
    static void NormalizeCodeBlocks(FlowDocument document) => NormalizeBlocks(document.Blocks);
    static void NormalizeBlocks(BlockCollection blocks)
    {
        foreach(var block in blocks.ToList()) {
            if(block is BlockUIContainer { Tag: "CodeBlock" } container && CodeText(container) is { } code) {
                var paragraph=new Paragraph(new Run(code)) { Tag="CodeBlock" };
                blocks.InsertAfter(container,paragraph); blocks.Remove(container);
                continue;
            }
            switch(block) {
                case Section section: NormalizeBlocks(section.Blocks); break;
                case List list: foreach(var item in list.ListItems) NormalizeBlocks(item.Blocks); break;
                case Table table: foreach(var group in table.RowGroups) foreach(var row in group.Rows) foreach(var cell in row.Cells) NormalizeBlocks(cell.Blocks); break;
            }
        }
    }
    static string? CodeText(BlockUIContainer container) => (container.Child as TextEditor)?.Text;
    static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) {
            var child=VisualTreeHelper.GetChild(root,i);
            if(child is ScrollViewer scroll) return scroll;
            if(FindScrollViewer(child) is { } found) return found;
        }
        return null;
    }
}
