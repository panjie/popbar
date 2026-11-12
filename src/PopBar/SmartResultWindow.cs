namespace PopBar;

internal sealed class SmartResultWindow:Window
{
    readonly TextBox output;
    readonly TextBlock status;
    readonly Button copy,replace;
    readonly ComboBox? mode;
    readonly SmartAction action;
    readonly Selection selection;
    readonly Settings settings;
    readonly Func<IReadOnlyList<MoneyAmount>,string,CancellationToken,Task<string>> convertCurrency;
    readonly bool isCurrency;
    CancellationTokenSource? pending;
    bool closed;
    internal SmartResultWindow(SmartAction action,Selection selection,Settings settings,
        Func<IReadOnlyList<MoneyAmount>,string,CancellationToken,Task<string>>? convertCurrency=null)
    {
        this.action=action; this.selection=selection; this.settings=settings;
        this.convertCurrency=convertCurrency??((amounts,target,token)=>CurrencyConversion.Convert(amounts,target,token));
        isCurrency=action.Kind==SmartActionKind.Currency;
        Title="PopBar · "+action.Label; Width=590; Height=430; MinWidth=380; MinHeight=260;
        WindowStartupLocation=WindowStartupLocation.CenterScreen; ShowInTaskbar=false; WindowStyle=WindowStyle.ToolWindow; Ui.Style(this);
        var root=new DockPanel { Margin=new Thickness(16) }; Content=root;
        var heading=new StackPanel { Margin=new Thickness(0,0,0,10) }; DockPanel.SetDock(heading,Dock.Top); root.Children.Add(heading);
        var source=new TextBlock { Text=selection.Text.Length>180?selection.Text[..180]+"…":selection.Text,TextTrimming=TextTrimming.CharacterEllipsis,MaxHeight=48,ToolTip=selection.Text.Length<=4096?selection.Text:null };
        source.SetResourceReference(TextBlock.ForegroundProperty,"MutedTextBrush"); heading.Children.Add(source);
        if(action.Kind==SmartActionKind.Json || isCurrency) {
            mode=new ComboBox { Name="SmartMode",Margin=new Thickness(0,10,0,0) }; mode.SetResourceReference(StyleProperty,"ComboBoxStyle");
            if(action.Kind==SmartActionKind.Json) {
                mode.Items.Add(T("SmartJson")); mode.Items.Add(T("SmartJsonCompact")); mode.SelectedIndex=0;
            } else {
                foreach(var currency in new[]{"CNY","JPY","USD"}) mode.Items.Add(currency);
                mode.SelectedItem=settings.CurrencyTarget;
                if(mode.SelectedIndex<0) mode.SelectedIndex=0;
            }
            System.Windows.Automation.AutomationProperties.SetName(mode,isCurrency?T("SmartCurrencyTarget"):T("SmartConversionMode"));
            heading.Children.Add(mode);
        }
        var footer=new StackPanel { Margin=new Thickness(0,10,0,0) }; DockPanel.SetDock(footer,Dock.Bottom); root.Children.Add(footer);
        status=new TextBlock { Name="SmartStatus",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,6) };
        status.SetResourceReference(TextBlock.ForegroundProperty,"MutedTextBrush"); footer.Children.Add(status);
        var buttons=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right }; footer.Children.Add(buttons);
        if(isCurrency) buttons.Children.Add(Ui.Button(T("SmartRetry"),()=>_=Update()));
        copy=Ui.Button(T("Copy"),()=>status.Text=Actions.Copy(output!.Text)?T("Copied"):T("ClipboardBusy")); copy.Name="SmartCopy"; buttons.Children.Add(copy);
        replace=Ui.Button(T("ReplaceOriginal"),async ()=> {
            replace!.IsEnabled=false;
            try { if(await Actions.Replace(selection,output!.Text)) { Close(); return; } status.Text=T("CannotVerifySelection"); }
            catch { status.Text=T("ReplaceFailed"); }
            finally { if(!closed) replace.IsEnabled=selection.Editable && output!.Text.Length>0; }
        },"PrimaryButtonStyle");
        replace.Name="SmartReplace"; replace.Visibility=selection.Editable?Visibility.Visible:Visibility.Collapsed; buttons.Children.Add(replace);
        output=Ui.Input("",true); output.Name="SmartOutput"; output.IsReadOnly=true; output.TextWrapping=TextWrapping.NoWrap;
        output.HorizontalScrollBarVisibility=ScrollBarVisibility.Auto;
        output.FontFamily=ResultTypography.ResolveFont(settings.ResultFontFamily); output.FontSize=ResultTypography.NormalizeSize(settings.ResultFontSize);
        root.Children.Add(output); copy.IsEnabled=replace.IsEnabled=false;
        if(mode!=null) mode.SelectionChanged+=(_,_)=>_=Update();
        Loaded+=(_,_)=>_=Update();
        Closed+=(_,_)=> { closed=true; pending?.Cancel(); };
    }
    async Task Update()
    {
        pending?.Cancel();
        using var request=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        pending=request;
        copy.IsEnabled=replace.IsEnabled=false;
        output.Clear();
        try {
            status.Text=T(isCurrency?"SmartRateLoading":"SmartLocalProcessing");
            string result;
            if(isCurrency) {
                var amounts=action.Amounts??MoneyParser.Parse(action.Target,settings.CurrencySource);
                result=await convertCurrency(amounts,mode!.SelectedItem as string??"CNY",request.Token);
                if(action.Amounts!=null)
                    result=string.Join("\n",action.Amounts.Select(amount=>T("SmartAiMoneySource",amount.Original)))+"\n\n"+result;
            }
            else result=action.Kind switch {
                SmartActionKind.Calculate=>SmartTransforms.Calculate(action.Target),
                SmartActionKind.Unit=>UnitConversion.Convert(action.Target),
                SmartActionKind.Json=>SmartTransforms.Json(action.Target,mode!.SelectedIndex==1),
                _=>throw new InvalidOperationException()
            };
            if(closed||pending!=request) return;
            output.Text=result;
            status.Text=T(action.Amounts!=null?"SmartAiMoneyReview":action.Source!=null?"SmartAiResultReview":isCurrency?"SmartRateHelp":"SmartLocalProcessing");
            if(action.Source!=null) status.Text+=" "+T("SmartAiMoneySource",action.Source.Length>180?action.Source[..180]+"…":action.Source);
            copy.IsEnabled=result.Length>0; replace.IsEnabled=selection.Editable && result.Length>0;
        } catch(Exception ex) {
            if(!closed&&pending==request)
                status.Text=isCurrency?T("SmartRateFailed"):T("SmartTransformFailed",ex.Message);
        } finally {
            if(pending==request) pending=null;
        }
    }
}
