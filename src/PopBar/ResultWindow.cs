using ICSharpCode.AvalonEdit;
using MdXaml;
using System.Text;
using System.Windows.Documents;

namespace PopBar;

internal sealed class ResultWindow:Window
{
    static readonly FontFamily BodyFont=new FontFamily("Segoe UI Variable, Segoe UI, Microsoft YaHei UI");
    static readonly FontFamily CodeFont=new FontFamily("Cascadia Code, Microsoft YaHei UI, Consolas");
    readonly MarkdownScrollViewer output=new();
    readonly StringBuilder markdown=new();
    readonly TextBlock status=Ui.Label("准备请求…",true);
    readonly Button retry,stop,replace;
    CancellationTokenSource? pending;
    readonly Settings settings; readonly PromptAction prompt; readonly Selection selection;
    long nextRender;
    int renderedLength;
    bool closed;
    internal ResultWindow(Settings settings,PromptAction prompt,Selection selection)
    {
        this.settings=settings; this.prompt=prompt; this.selection=selection;
        Title=$"PopBar · {prompt.Name}"; Width=680; Height=600; MinWidth=480; MinHeight=360; Ui.Style(this); WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var root=new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        Content=root;

        var heading=new StackPanel { Margin=new Thickness(24,22,24,16) }; root.Children.Add(heading);
        var title=new TextBlock { Text=prompt.Name,FontSize=23,FontWeight=FontWeights.SemiBold };
        title.SetResourceReference(TextBlock.ForegroundProperty,"TextBrush"); heading.Children.Add(title);
        var metadata=new TextBlock { Text=$"{settings.Model}  ·  已选 {selection.Text.Length:N0} 字符",FontSize=12,Margin=new Thickness(0,6,0,0) };
        metadata.SetResourceReference(TextBlock.ForegroundProperty,"MutedTextBrush"); heading.Children.Add(metadata);

        output.Name="OutputMarkdown"; output.IsToolBarVisible=false; output.IsSelectionEnabled=true;
        output.VerticalScrollBarVisibility=ScrollBarVisibility.Auto; output.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;
        output.ClickAction=ClickAction.SafetyOpenBrowser; output.MarkdownStyle=MarkdownDocumentStyle();
        output.FontFamily=BodyFont; output.FontSize=14;
        output.SetResourceReference(Control.ForegroundProperty,"TextBrush"); output.SetResourceReference(Control.BackgroundProperty,"SurfaceBrush");
        var outputHost=new Border { Margin=new Thickness(24,0,24,0),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(6),Padding=new Thickness(14,10,8,10),Child=output };
        outputHost.SetResourceReference(Border.BackgroundProperty,"SurfaceBrush"); outputHost.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");
        Grid.SetRow(outputHost,1); root.Children.Add(outputHost);

        status.Name="StatusText"; status.Margin=new Thickness(27,12,27,10); Grid.SetRow(status,2); root.Children.Add(status);
        var footer=new Border { BorderThickness=new Thickness(0,1,0,0),Padding=new Thickness(20,10,20,10) }; Grid.SetRow(footer,3); root.Children.Add(footer);
        footer.SetResourceReference(Border.BackgroundProperty,"SurfaceBrush");
        footer.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");
        var buttons=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right }; footer.Child=buttons;
        stop=Ui.Button("停止",()=>pending?.Cancel(),"DangerButtonStyle"); stop.Name="StopButton"; buttons.Children.Add(stop);
        retry=Ui.Button("重试",()=>_=Run()); retry.Name="RetryButton"; buttons.Children.Add(retry);
        var copy=Ui.Button("复制",()=> { status.Text=Actions.Copy(PlainResult())?"已复制到剪贴板。":"剪贴板正忙，请重试。"; }); copy.Name="CopyResultButton"; buttons.Children.Add(copy);
        replace=Ui.Button("替换原文",async ()=> {
            replace!.IsEnabled=false;
            try { if(await Actions.Replace(selection,PlainResult())) { Close(); return; } status.Text="原选区已改变或无法确认可编辑，请复制后手动粘贴。"; }
            catch { status.Text="无法替换，请复制后手动粘贴。"; }
        },"PrimaryButtonStyle"); replace.Name="ReplaceButton"; buttons.Children.Add(replace);
        var closeButton=Ui.Button("关闭",Close,"SubtleButtonStyle"); closeButton.Name="CloseResultButton"; buttons.Children.Add(closeButton);
        replace.IsEnabled=false;
        Loaded+=async (_,_)=>await Run(); Closed+=(_,_)=> { closed=true; pending?.Cancel(); };
        PreviewKeyDown+=(_,e)=> { if(e.Key==System.Windows.Input.Key.Escape) Close(); };
    }
    static Style MarkdownDocumentStyle()
    {
        var style=new Style(typeof(FlowDocument),MarkdownStyle.SasabuneCompact);
        style.Setters.Add(new Setter(FlowDocument.FontFamilyProperty,BodyFont));
        style.Setters.Add(new Setter(FlowDocument.FontSizeProperty,14.0));
        return style;
    }
    async Task Run()
    {
        pending?.Dispose(); pending=new(); var token=pending.Token;
        markdown.Clear(); renderedLength=0; nextRender=0; Display(""); retry.IsEnabled=false; replace.IsEnabled=false; stop.IsEnabled=true; status.Text="正在生成…";
        try {
            await AiClient.Stream(settings,prompt,selection.Text,chunk=> { if(!closed) Append(chunk); },token);
            if(!closed) { Render(true); status.Text="完成 · 替换前会重新确认原选区。"; replace.IsEnabled=selection.Editable; }
        } catch(OperationCanceledException) { if(!closed) { Render(true); status.Text="已停止或超时 · 部分结果未完成，可手动复制。"; } }
        catch(Exception ex) { if(!closed) { Render(true); status.Text=ex is InvalidOperationException or InvalidDataException ? ex.Message : "请求失败，请检查网络和接口设置。"; } }
        finally { if(!closed) { retry.IsEnabled=true; stop.IsEnabled=false; } }
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
    }    internal void RenderMarkdown(string source)
    {
        markdown.Clear(); markdown.Append(source); nextRender=0;
        Display(source);
    }
    void Display(string source)
    {
        var safe=MarkdownText.DemoteImages(source);
        try { output.Markdown=safe; NormalizeCodeBlocks(output.Document!); }
        catch {
            try {
                var fallback=new FlowDocument(new Paragraph(new Run(safe))) { FontFamily=BodyFont,FontSize=14 };
                fallback.SetResourceReference(TextElement.ForegroundProperty,"TextBrush");
                output.Document=fallback;
            } catch { }
        }
        renderedLength=source.Length;
        try { ScrollToEnd(output); } catch { }
    }
    internal static void ScrollToEnd(MarkdownScrollViewer viewer)
    {
        viewer.UpdateLayout();
        FindScrollViewer(viewer)?.ScrollToEnd();
        viewer.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(()=> { try { FindScrollViewer(viewer)?.ScrollToEnd(); } catch { } }));
    }
    static void NormalizeCodeBlocks(FlowDocument document) => NormalizeBlocks(document.Blocks);
    static void NormalizeBlocks(BlockCollection blocks)
    {
        foreach(var block in blocks.ToList()) {
            if(block is BlockUIContainer { Tag: "CodeBlock" } container && CodeText(container) is { } code) {
                var paragraph=new Paragraph(new Run(code)) { Tag="CodeBlock",FontFamily=CodeFont };
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
