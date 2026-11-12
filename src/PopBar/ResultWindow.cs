using ICSharpCode.AvalonEdit;
using MdXaml;
using System.Text;
using System.Windows.Documents;
using System.Windows.Threading;

namespace PopBar;

internal delegate Task ConversationStreamer(
    Settings settings,
    IReadOnlyList<ChatMessage> messages,
    Action<string> append,
    CancellationToken token,
    Action? onThinking);

internal sealed class ResultWindow:ResultPopupWindow
{
    internal const int MaxConversationCharacters=262144;
    readonly MarkdownScrollViewer output=new();
    readonly StringBuilder latestAnswer=new();
    readonly List<ChatMessage> history=new();
    readonly TextBlock status=new() { Name="StatusText",TextTrimming=TextTrimming.CharacterEllipsis };
    readonly Button stop,replace,copy,send,followUpAction;
    readonly TextBox followUp;
    readonly TextBlock followUpPlaceholder=new() {
        Margin=new Thickness(12,0,10,0),VerticalAlignment=VerticalAlignment.Center,
        IsHitTestVisible=false,TextTrimming=TextTrimming.CharacterEllipsis
    };
    readonly Border composer;
    readonly ConversationStreamer streamer;
    string? statusKey="PreparingRequest";
    string? preferredFont;
    readonly DispatcherTimer autoCloseTimer=new() { Interval=TimeSpan.FromSeconds(5) };
    bool autoCloseOnDeactivate;
    bool followUpMode;
    bool conversationStarted;
    CancellationTokenSource? pending;
    Settings settings; readonly PromptAction prompt; readonly Selection selection;
    long nextRender;
    int renderedLength;
    bool closed;
    internal ResultWindow(Settings settings,PromptAction prompt,Selection selection,ConversationStreamer? streamer=null)
        :base(PromptIcons.Create(prompt,out _))
    {
        this.settings=settings; this.prompt=prompt; this.selection=selection;
        this.streamer=streamer??((requestSettings,messages,append,token,onThinking)=>
            AiClient.Stream(requestSettings,messages,append,token,onThinking:onThinking));
        autoCloseOnDeactivate=settings.AutoCloseResultOnDeactivate;
        Width=580; Height=460; MinWidth=380; MinHeight=260;
        var root=new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        SetResultContent(root);

        output.Name="OutputMarkdown"; output.IsToolBarVisible=false; output.IsSelectionEnabled=true;
        output.VerticalScrollBarVisibility=ScrollBarVisibility.Auto; output.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;
        output.ClickAction=ClickAction.SafetyOpenBrowser;
        output.MarkdownStyle=(Style)FindResource("ResultMarkdownStyle");
        ApplyTypography(settings);
        output.SetResourceReference(Control.ForegroundProperty,"TextBrush");
        output.SetResourceReference(Control.BackgroundProperty,"SurfaceBrush");
        root.Children.Add(output);

        composer=new Border {
            Name="FollowUpComposer",Visibility=Visibility.Collapsed,
            BorderThickness=new Thickness(0,1,0,0),Padding=new Thickness(16,7,10,7)
        };
        composer.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");
        composer.SetResourceReference(Border.BackgroundProperty,"WindowBackgroundBrush");
        Grid.SetRow(composer,1); root.Children.Add(composer);
        var composerContent=new DockPanel { LastChildFill=true };
        composer.Child=composerContent;
        send=Ui.Button("",()=>_=SendFollowUp(),"PrimaryButtonStyle");
        send.Name="SendFollowUpButton"; send.MinHeight=32; send.Padding=new Thickness(12,4,12,4);
        send.Margin=new Thickness(8,0,0,0); send.VerticalAlignment=VerticalAlignment.Bottom;
        DockPanel.SetDock(send,Dock.Right); composerContent.Children.Add(send);
        followUp=Ui.Input("",true);
        followUp.Name="FollowUpInput"; followUp.MinHeight=32; followUp.MaxHeight=96;
        followUp.VerticalContentAlignment=VerticalAlignment.Center;
        followUp.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;
        followUp.PreviewKeyDown+=(_,e)=> {
            var modifiers=System.Windows.Input.Keyboard.Modifiers;
            if(e.Key==System.Windows.Input.Key.Enter &&
               (modifiers&System.Windows.Input.ModifierKeys.Shift)==0) {
                e.Handled=true;
                _=HandleFollowUpKeyAsync(e.Key,modifiers);
            }
        };
        followUp.TextChanged+=(_,_)=> {
            followUpPlaceholder.Visibility=followUp.Text.Length==0?Visibility.Visible:Visibility.Collapsed;
            UpdateComposer();
        };
        followUpPlaceholder.SetResourceReference(TextBlock.ForegroundProperty,"MutedTextBrush");
        composerContent.Children.Add(new Grid { Children={followUp,followUpPlaceholder} });

        var footer=CreateResultFooter(status,out var buttons);
        Grid.SetRow(footer,2); root.Children.Add(footer);
        stop=AddResultAction(buttons,"StopButton","",()=>pending?.Cancel(),"DangerButtonStyle");
        followUpAction=AddResultAction(buttons,"FollowUpButton","",StartFollowUp);
        copy=AddResultAction(buttons,"CopyResultButton","",()=> {
            SetStatus(Actions.Copy(PlainResult())?"Copied":"ClipboardBusy");
        });
        replace=AddResultAction(buttons,"ReplaceButton","",async ()=> {
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
        replace.Visibility=stop.Visibility=followUpAction.Visibility=Visibility.Collapsed;
        RefreshLanguage();

        Activated+=(_,_)=>autoCloseTimer.Stop();
        Deactivated+=(_,_)=> { if(autoCloseOnDeactivate && !followUpMode && !closed) autoCloseTimer.Start(); };
        autoCloseTimer.Tick+=(_,_)=> {
            autoCloseTimer.Stop();
            if(autoCloseOnDeactivate && !followUpMode && !IsActive && !closed) Close();
        };
        Loaded+=async (_,_)=>await Run();
        Closed+=(_,_)=> {
            closed=true;
            autoCloseTimer.Stop();
            pending?.Cancel();
        };
    }
    internal void RefreshLanguage()
    {
        BuiltInPrompts.LocalizeName(prompt);
        Ui.ApplyTextLanguage(this);
        ApplyResultFont();
        SetResultTitle(prompt.Name,settings.Model);
        stop.Content=T("Stop");
        followUpAction.Content=T("FollowUp");
        copy.Content=T("Copy");
        replace.Content=T("ReplaceOriginal");
        send.Content=T("SendFollowUp");
        followUpPlaceholder.Text=T("FollowUpPlaceholder");
        followUp.ToolTip=T("FollowUpPrompt");
        System.Windows.Automation.AutomationProperties.SetName(followUp,T("FollowUpPrompt"));
        if(statusKey!=null) status.Text=T(statusKey);
        UpdateComposer();
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
    void ClearStatus()
    {
        statusKey=null;
        status.Text="";
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
    internal void ApplySettings(Settings preferences)
    {
        settings=preferences;
        RefreshLanguage();
    }
    internal void SetAutoCloseOnDeactivate(bool enabled)
    {
        if(autoCloseOnDeactivate==enabled || closed) return;
        autoCloseOnDeactivate=enabled;
        if(enabled && !followUpMode && IsVisible && !IsActive) autoCloseTimer.Start();
        else autoCloseTimer.Stop();
    }
    internal async Task Run()
    {
        if(pending!=null || closed) return;
        history.Clear();
        history.Add(new ChatMessage("user",AiClient.BuildPrompt(prompt.Prompt,selection.Text)));
        latestAnswer.Clear();
        conversationStarted=false;
        await StreamCurrent();
    }
    void StartFollowUp()
    {
        if(pending!=null || closed || followUpMode) return;
        followUpMode=true;
        autoCloseTimer.Stop();
        composer.Visibility=Visibility.Visible;
        followUpAction.Visibility=Visibility.Collapsed;
        followUp.Focus();
    }
    internal async Task SendFollowUp()
    {
        if(!followUpMode || pending!=null || closed || history.Count==0) return;
        var question=followUp.Text;
        if(string.IsNullOrWhiteSpace(question)) { UpdateComposer(); return; }
        if(history.Sum(message=>message.Content.Length)+question.Length>MaxConversationCharacters) {
            SetStatus("ConversationTooLong");
            return;
        }
        history.Add(new ChatMessage("user",question));
        latestAnswer.Clear();
        followUp.Clear();
        conversationStarted=true;
        autoCloseTimer.Stop();
        await StreamCurrent();
    }
    internal async Task<bool> HandleFollowUpKeyAsync(System.Windows.Input.Key key,System.Windows.Input.ModifierKeys modifiers)
    {
        if(key!=System.Windows.Input.Key.Enter ||
           (modifiers&System.Windows.Input.ModifierKeys.Shift)!=0) return false;
        await SendFollowUp();
        return true;
    }
    async Task StreamCurrent()
    {
        if(pending!=null || closed || history.Count==0) return;
        using var request=new CancellationTokenSource();
        pending=request;
        renderedLength=0; nextRender=0;
        DisplayTranscript();
        followUpAction.Visibility=replace.Visibility=Visibility.Collapsed;
        stop.Visibility=Visibility.Visible;
        UpdateComposer();
        SetStatus("WaitingForResponse");
        try {
            await streamer(settings,history.ToArray(),chunk=> {
                if(closed) return;
                if(latestAnswer.Length==0) SetStatus("StreamingResult");
                Append(chunk);
            },request.Token,onThinking:()=> {
                if(!closed) SetStatus("ModelThinking");
            });
            if(!closed) {
                CommitAnswer();
                Render(true);
                ClearStatus();
                replace.IsEnabled=selection.Editable;
                replace.Visibility=selection.Editable?Visibility.Visible:Visibility.Collapsed;
            }
        } catch(OperationCanceledException) {
            if(!closed) { CommitAnswer(); Render(true); SetStatus("RequestStopped"); }
        }
        catch(Exception ex) {
            if(!closed) {
                CommitAnswer();
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
                followUpAction.Visibility=followUpMode?Visibility.Collapsed:Visibility.Visible;
                stop.Visibility=Visibility.Collapsed;
                UpdateComposer();
                if(followUpMode && IsActive) followUp.Focus();
            }
        }
    }
    void CommitAnswer()
    {
        var answer=latestAnswer.ToString();
        if(answer.Length==0) return;
        if(history.Count>0 && history[^1].Role=="assistant")
            history[^1]=new ChatMessage("assistant",answer);
        else history.Add(new ChatMessage("assistant",answer));
    }
    void UpdateComposer() =>
        send.IsEnabled=pending==null && history.Count>0 && !string.IsNullOrWhiteSpace(followUp.Text);
    internal void Append(string chunk)
    {
        latestAnswer.Append(chunk); Render();
    }
    internal string PlainResult()
    {
        if(latestAnswer.Length==0) return "";
        var source=MarkdownText.DemoteImages(latestAnswer.ToString());
        try {
            var viewer=new MarkdownScrollViewer { MarkdownStyle=output.MarkdownStyle };
            viewer.Markdown=source;
            NormalizeCodeBlocks(viewer.Document!);
            return MarkdownText.Plain(viewer.Document);
        } catch { return latestAnswer.ToString(); }
    }
    string Transcript()
    {
        if(history.Count==0) return latestAnswer.ToString();
        var turns=history.Skip(1).ToList();
        if(latestAnswer.Length>0 && turns.Count>0 && turns[^1].Role=="assistant")
            turns.RemoveAt(turns.Count-1);
        var source=new StringBuilder();
        foreach(var message in turns)
            source.Append(message.Role=="user"
                ?MarkdownText.FormatUserTurn(T("You"),message.Content)
                :MarkdownText.FormatAssistantTurn(T("Assistant"),message.Content));
        if(latestAnswer.Length>0)
            source.Append(history.Count<=1 || !conversationStarted
                ?latestAnswer.ToString()
                :MarkdownText.FormatAssistantTurn(T("Assistant"),latestAnswer.ToString()));
        return source.ToString();
    }
    void DisplayTranscript() => Display(Transcript());
    void Render(bool force=false)
    {
        if(closed) return;
        if(!force && Environment.TickCount64<nextRender) return;
        var source=Transcript();
        if(source.Length==renderedLength) return;
        var started=Environment.TickCount64;
        Display(source);
        var elapsed=Environment.TickCount64-started;
        nextRender=Environment.TickCount64+Math.Max(120,elapsed*8);
    }
    internal void RenderMarkdown(string source)
    {
        history.Clear(); latestAnswer.Clear().Append(source); nextRender=0;
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
