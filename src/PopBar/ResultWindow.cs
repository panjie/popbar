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

        output.Name="OutputTextBox"; output.Margin=new Thickness(24,0,24,0); output.IsReadOnly=true; output.FontFamily=new FontFamily("Cascadia Code, Microsoft YaHei UI"); output.FontSize=14; output.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;
        Grid.SetRow(output,1); root.Children.Add(output);

        status.Name="StatusText"; status.Margin=new Thickness(27,12,27,10); Grid.SetRow(status,2); root.Children.Add(status);
        var footer=new Border { BorderThickness=new Thickness(0,1,0,0),Padding=new Thickness(20,10,20,10) }; Grid.SetRow(footer,3); root.Children.Add(footer);
        footer.SetResourceReference(Border.BackgroundProperty,"SurfaceBrush");
        footer.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");
        var buttons=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right }; footer.Child=buttons;
        stop=Ui.Button("停止",()=>pending?.Cancel(),"DangerButtonStyle"); stop.Name="StopButton"; buttons.Children.Add(stop);
        retry=Ui.Button("重试",()=>_=Run()); retry.Name="RetryButton"; buttons.Children.Add(retry);
        var copy=Ui.Button("复制",()=> { status.Text=Actions.Copy(output.Text)?"已复制到剪贴板。":"剪贴板正忙，请重试。"; }); copy.Name="CopyResultButton"; buttons.Children.Add(copy);
        replace=Ui.Button("替换原文",async ()=> {
            replace!.IsEnabled=false;
            try { if(await Actions.Replace(selection,output.Text)) { Close(); return; } status.Text="原选区已改变或无法确认可编辑，请复制后手动粘贴。"; }
            catch { status.Text="无法替换，请复制后手动粘贴。"; }
        },"PrimaryButtonStyle"); replace.Name="ReplaceButton"; buttons.Children.Add(replace);
        var closeButton=Ui.Button("关闭",Close,"SubtleButtonStyle"); closeButton.Name="CloseResultButton"; buttons.Children.Add(closeButton);
        replace.IsEnabled=false;
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
