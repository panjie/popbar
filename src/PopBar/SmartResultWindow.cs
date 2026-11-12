namespace PopBar;

internal sealed class SmartResultWindow:ResultPopupWindow
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
        :base(new TextBlock {
            Text=action.Glyph,FontSize=16,
            FontFamily=action.Kind is SmartActionKind.Currency or SmartActionKind.Unit?UiTypography.ForLanguage(UiText.Culture):UiTypography.Icons
        })
    {
        this.action=action; this.selection=selection; this.settings=settings;
        this.convertCurrency=convertCurrency??((amounts,target,token)=>CurrencyConversion.Convert(amounts,target,token));
        isCurrency=action.Kind==SmartActionKind.Currency;
        var compact=action.Kind is SmartActionKind.Calculate or SmartActionKind.Unit;
        SetResultTitle(action.Label);
        Width=compact?480:610; Height=compact?300:450; MinWidth=380; MinHeight=260;
        var root=new DockPanel(); SetResultContent(root);
        var heading=new DockPanel { Margin=new Thickness(16,12,16,4) };
        DockPanel.SetDock(heading,Dock.Top); root.Children.Add(heading);
        var source=new TextBlock {
            Text=selection.Text.Length>180?selection.Text[..180]+"…":selection.Text,TextTrimming=TextTrimming.CharacterEllipsis,
            MaxHeight=48,VerticalAlignment=VerticalAlignment.Center,ToolTip=selection.Text.Length<=4096?selection.Text:null
        };
        source.SetResourceReference(TextBlock.ForegroundProperty,"MutedTextBrush");
        if(action.Kind==SmartActionKind.Json || isCurrency) {
            mode=new ComboBox { Name="SmartMode",MinWidth=88,MaxWidth=170,Margin=new Thickness(8,0,0,0) };
            mode.SetResourceReference(StyleProperty,"ComboBoxStyle");
            if(action.Kind==SmartActionKind.Json) {
                mode.Items.Add(T("SmartJson")); mode.Items.Add(T("SmartJsonCompact")); mode.SelectedIndex=0;
            } else {
                foreach(var currency in new[]{"CNY","JPY","USD"}) mode.Items.Add(currency);
                mode.SelectedItem=settings.CurrencyTarget;
                if(mode.SelectedIndex<0) mode.SelectedIndex=0;
            }
            System.Windows.Automation.AutomationProperties.SetName(mode,isCurrency?T("SmartCurrencyTarget"):T("SmartConversionMode"));
            DockPanel.SetDock(mode,Dock.Right);
            heading.Children.Add(mode);
        }
        if(action.Kind!=SmartActionKind.Unit || selection.Text.Length>180 || !UnitConversion.IsQuantity(selection.Text.Trim()))
            heading.Children.Add(source);
        status=new TextBlock { Name="SmartStatus",TextWrapping=TextWrapping.Wrap,TextTrimming=TextTrimming.CharacterEllipsis,MaxHeight=54 };
        var footer=CreateResultFooter(status,out var buttons);
        DockPanel.SetDock(footer,Dock.Bottom); root.Children.Add(footer);
        if(isCurrency) AddResultAction(buttons,"SmartRetry",T("SmartRetry"),()=>_=Update());
        copy=AddResultAction(buttons,"SmartCopy",T("Copy"),()=>status.Text=Actions.Copy(output!.Text)?T("Copied"):T("ClipboardBusy"));
        replace=AddResultAction(buttons,"SmartReplace",T("ReplaceOriginal"),async ()=> {
            replace!.IsEnabled=false;
            try { if(await Actions.Replace(selection,output!.Text)) { Close(); return; } status.Text=T("CannotVerifySelection"); }
            catch { status.Text=T("ReplaceFailed"); }
            finally { if(!closed) replace.IsEnabled=selection.Editable && output!.Text.Length>0; }
        },"PrimaryButtonStyle");
        replace.Visibility=selection.Editable?Visibility.Visible:Visibility.Collapsed;
        output=Ui.Input("",true); output.Name="SmartOutput"; output.IsReadOnly=true; output.TextWrapping=TextWrapping.NoWrap;
        output.HorizontalScrollBarVisibility=ScrollBarVisibility.Auto;
        output.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;
        output.VerticalContentAlignment=VerticalAlignment.Top;
        output.SetResourceReference(StyleProperty,"ResultOutputTextBoxStyle");
        output.Margin=new Thickness(13,3,12,8);
        output.Padding=new Thickness(0,4,0,4);
        output.FontFamily=ResultTypography.ResolveFont(settings.ResultFontFamily);
        var size=ResultTypography.NormalizeSize(settings.ResultFontSize);
        output.FontSize=compact?Math.Min(ResultTypography.MaxSize,size+4):size;
        if(compact) output.FontWeight=FontWeights.SemiBold;
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
            status.Text=isCurrency?T("SmartRateLoading"):"";
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
            status.Text=action.Amounts!=null?T("SmartAiMoneyReview"):action.Source!=null?T("SmartAiResultReview"):isCurrency?T("SmartRateHelp"):"";
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
