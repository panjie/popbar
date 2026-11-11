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
    readonly TextBlock status=new() { Text="准备请求…",FontSize=12,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center };
    readonly Button retry,stop,replace;
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
        Title=$"PopBar · {prompt.Name}";
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

        var metadata=new TextBlock {
            Text=$"{settings.Model}  ·  已选 {selection.Text.Length:N0} 字符",FontSize=11,
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
        stop=ActionButton("StopButton","停止",()=>pending?.Cancel(),"DangerButtonStyle");
        retry=ActionButton("RetryButton","重试",()=>_=Run());
        ActionButton("CopyResultButton","复制",()=> {
            status.Text=Actions.Copy(PlainResult())?"已复制到剪贴板。":"剪贴板正忙，请重试。";
        });
        replace=ActionButton("ReplaceButton","替换原文",async ()=> {
            replace!.IsEnabled=false;
            try {
                var replaced=await Actions.Replace(selection,PlainResult());
                if(closed) return;
                if(replaced) { Close(); return; }
                status.Text="原选区已改变或无法确认可编辑，请复制后手动粘贴。";
            }
            catch { status.Text="无法替换，请复制后手动粘贴。"; }
        },"PrimaryButtonStyle");
        replace.IsEnabled=false;
        replace.Visibility=stop.Visibility=Visibility.Collapsed;

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
    internal void ApplyTypography(Settings preferences)
    {
        var size=ResultTypography.NormalizeSize(preferences.ResultFontSize);
        var font=ResultTypography.ResolveFont(preferences.ResultFontFamily);
        output.FontFamily=font;
        output.FontSize=size;
        output.Resources["ResultBodyFont"]=font;
        output.Resources["ResultCodeFont"]=ReferenceEquals(font,ResultTypography.DefaultFont)?ResultTypography.DefaultCodeFont:font;
        output.Resources["ResultBodySize"]=(double)size;
        output.Resources["ResultLineHeight"]=Math.Round(size*20.0/ResultTypography.DefaultSize);
        output.Resources["ResultHeading1Size"]=(double)(size+6);
        output.Resources["ResultHeading2Size"]=(double)(size+3);
        output.Resources["ResultHeading3Size"]=(double)(size+1);
        output.Resources["ResultCodeSize"]=(double)(size-1);
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
        status.Text="等待服务响应…";
        try {
            await AiClient.Stream(settings,prompt,selection.Text,chunk=> {
                if(closed) return;
                if(markdown.Length==0) status.Text="正在输出…";
                Append(chunk);
            },request.Token,onThinking:()=> {
                if(!closed) status.Text="模型思考中…";
            });
            if(!closed) {
                Render(true);
                status.Text="已完成";
                replace.IsEnabled=selection.Editable;
                replace.Visibility=selection.Editable?Visibility.Visible:Visibility.Collapsed;
            }
        } catch(OperationCanceledException) { if(!closed) { Render(true); status.Text="已停止或超时 · 部分结果未完成，可手动复制。"; } }
        catch(Exception ex) { if(!closed) { Render(true); status.Text=ex is InvalidOperationException or InvalidDataException ? ex.Message : "请求失败，请检查网络和接口设置。"; } }
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
