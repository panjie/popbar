namespace PopBar;

internal sealed class ResultWindow:Window
{
    readonly TextBox output=Ui.Input("",true);
    readonly TextBlock status=Ui.Label("准备请求…",true);
    readonly Button retry,stop,replace;
    CancellationTokenSource? pending;
    readonly Settings settings; readonly PromptAction prompt; readonly Selection selection;
    bool closed;
    internal ResultWindow(Settings settings,PromptAction prompt,Selection selection)
    {
        this.settings=settings; this.prompt=prompt; this.selection=selection;
        Title=$"PopBar · {prompt.Name}"; Width=640; Height=550; MinWidth=420; MinHeight=300; Ui.Style(this); WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var root=new DockPanel { Margin=new Thickness(20) }; Content=root;
        var heading=new StackPanel(); DockPanel.SetDock(heading,Dock.Top); root.Children.Add(heading);
        heading.Children.Add(new TextBlock { Text=prompt.Name,FontSize=24,Foreground=Ui.Accent });
        heading.Children.Add(Ui.Label($"{settings.Model}  ·  已选 {selection.Text.Length:N0} 字符",true));
        var footer=new StackPanel(); DockPanel.SetDock(footer,Dock.Bottom); root.Children.Add(footer); footer.Children.Add(status);
        var buttons=new WrapPanel(); footer.Children.Add(buttons);
        stop=Ui.Button("停止",()=>pending?.Cancel()); buttons.Children.Add(stop);
        retry=Ui.Button("重试",()=>_=Run()); buttons.Children.Add(retry);
        buttons.Children.Add(Ui.Button("复制",()=> { status.Text=Actions.Copy(output.Text)?"已复制到剪贴板。":"剪贴板正忙，请重试。"; }));
        replace=Ui.Button("替换原文",async ()=> {
            replace!.IsEnabled=false;
            try { if(await Actions.Replace(selection,output.Text)) { Close(); return; } status.Text="原选区已改变或无法确认可编辑，请复制后手动粘贴。"; }
            catch { status.Text="无法替换，请复制后手动粘贴。"; }
        }); buttons.Children.Add(replace);
        buttons.Children.Add(Ui.Button("关闭",Close));
        output.IsReadOnly=true; output.FontFamily=new FontFamily("Cascadia Code, Microsoft YaHei UI"); output.FontSize=14; output.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;
        root.Children.Add(output);
        Loaded+=async (_,_)=>await Run(); Closed+=(_,_)=> { closed=true; pending?.Cancel(); };
        PreviewKeyDown+=(_,e)=> { if(e.Key==System.Windows.Input.Key.Escape) Close(); };
    }
    async Task Run()
    {
        pending?.Dispose(); pending=new(); var token=pending.Token;
        output.Clear(); retry.IsEnabled=false; replace.IsEnabled=false; stop.IsEnabled=true; status.Text="正在生成…";
        try {
            await AiClient.Stream(settings,prompt,selection.Text,chunk=> { if(!closed) { output.AppendText(chunk); output.ScrollToEnd(); } },token);
            if(!closed) { status.Text="完成 · 替换前会重新确认原选区。"; replace.IsEnabled=selection.Editable; }
        } catch(OperationCanceledException) { if(!closed) status.Text="已停止或超时 · 部分结果未完成，可手动复制。"; }
        catch(Exception ex) { if(!closed) status.Text=ex is InvalidOperationException or InvalidDataException ? ex.Message : "请求失败，请检查网络和接口设置。"; }
        finally { if(!closed) { retry.IsEnabled=true; stop.IsEnabled=false; } }
    }
}
