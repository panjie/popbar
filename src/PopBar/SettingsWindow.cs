namespace PopBar;

internal sealed class SettingsWindow:Window
{
    sealed record LanguageOption(UiLanguage Value,string Label,FontFamily Font,System.Windows.Markup.XmlLanguage Language);

    internal SettingsWindow(Settings settings,Action<Settings> saved)
    {
        Title=T("SettingsWindowTitle"); Width=820; Height=680; MinWidth=680; MinHeight=480; WindowStartupLocation=WindowStartupLocation.CenterScreen; Ui.Style(this);
        var root=new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        Content=root;

        TextBlock Text(string text,double size=13,bool muted=false,FontWeight? weight=null)
        {
            var block=new TextBlock { Text=text,FontSize=size,FontWeight=weight??FontWeights.Normal,TextWrapping=TextWrapping.Wrap };
            block.SetResourceReference(TextBlock.ForegroundProperty,muted?"MutedTextBrush":"TextBrush");
            return block;
        }
        TextBlock FieldLabel(StackPanel panel,string text)
        {
            var label=Text(text,13,false,FontWeights.SemiBold); label.Margin=new Thickness(0,12,0,6); panel.Children.Add(label); return label;
        }
        TextBox Field(StackPanel panel,string label,string value,string hint="",bool multiline=false)
        {
            FieldLabel(panel,label); var box=Ui.Input(value,multiline); panel.Children.Add(box);
            if(hint.Length>0) { var help=Text(hint,12,true); help.Margin=new Thickness(0,6,0,0); panel.Children.Add(help); }
            return box;
        }
        TextBlock Section(StackPanel panel,string text)
        {
            var heading=Text(text,15,false,FontWeights.SemiBold); heading.Margin=new Thickness(0,22,0,4); panel.Children.Add(heading); return heading;
        }

        var body=new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(188) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
        root.Children.Add(body);
        var sidebar=new Border { Padding=new Thickness(18,24,14,20) };
        sidebar.SetResourceReference(Border.BackgroundProperty,"SecondarySurfaceBrush");
        body.Children.Add(sidebar);
        var navigation=new StackPanel();
        var sideContent=new DockPanel(); sidebar.Child=sideContent;
        var brand=new StackPanel { Margin=new Thickness(8,0,4,28) }; DockPanel.SetDock(brand,Dock.Top); sideContent.Children.Add(brand);
        var appName=Text("PopBar",23,false,FontWeights.SemiBold); brand.Children.Add(appName);
        var subtitle=Text(T("Tagline"),12,true); subtitle.Margin=new Thickness(0,5,0,0); brand.Children.Add(subtitle);
        sideContent.Children.Add(navigation);

        var pages=new Grid { Margin=new Thickness(30,24,30,18) }; Grid.SetColumn(pages,1); body.Children.Add(pages);
        (Grid Page,StackPanel Content) StandardPage(string name,string title,string description)
        {
            var page=new Grid { Name=name,Visibility=Visibility.Collapsed };
            var dock=new DockPanel(); page.Children.Add(dock);
            var heading=new StackPanel { Margin=new Thickness(0,0,0,20) }; DockPanel.SetDock(heading,Dock.Top); dock.Children.Add(heading);
            heading.Children.Add(Text(title,24,false,FontWeights.SemiBold));
            var summary=Text(description,12,true); summary.Margin=new Thickness(0,6,0,0); heading.Children.Add(summary);
            var content=new StackPanel { Margin=new Thickness(0,0,10,0) };
            dock.Children.Add(new ScrollViewer { Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled });
            pages.Children.Add(page); return (page,content);
        }

        var (generalPage,general)=StandardPage("GeneralPage",T("General"),T("GeneralDescription"));
        var (aiPage,ai)=StandardPage("AiPage",T("AiService"),T("AiServiceDescription"));
        var promptsPage=new Grid { Name="PromptsPage",Visibility=Visibility.Collapsed }; pages.Children.Add(promptsPage);

        Button Navigation(string name,string glyph,string label)
        {
            var content=new StackPanel { Orientation=Orientation.Horizontal };
            content.Children.Add(new TextBlock { Text=glyph,FontFamily=UiTypography.Icons,FontSize=16,VerticalAlignment=VerticalAlignment.Center });
            content.Children.Add(new TextBlock { Text=label,Margin=new Thickness(11,0,0,0),VerticalAlignment=VerticalAlignment.Center });
            var button=new Button { Name=name,Content=content }; button.SetResourceReference(FrameworkElement.StyleProperty,"NavigationButtonStyle"); navigation.Children.Add(button); return button;
        }
        var generalNav=Navigation("GeneralNavigationButton","\uE80F",T("General"));
        var aiNav=Navigation("AiNavigationButton","\uE73E",T("AiService"));
        var promptsNav=Navigation("PromptsNavigationButton","\uE70F",T("Prompts"));
        void ShowPage(Grid selected,Button selectedNav)
        {
            generalPage.Visibility=selected==generalPage?Visibility.Visible:Visibility.Collapsed;
            aiPage.Visibility=selected==aiPage?Visibility.Visible:Visibility.Collapsed;
            promptsPage.Visibility=selected==promptsPage?Visibility.Visible:Visibility.Collapsed;
            foreach(var button in new[]{generalNav,aiNav,promptsNav}) {
                button.Background=Brushes.Transparent;
                if(button==selectedNav) button.SetResourceReference(Button.BackgroundProperty,"SurfaceBrush");
            }
        }
        generalNav.Click+=(_,_)=>ShowPage(generalPage,generalNav);
        aiNav.Click+=(_,_)=>ShowPage(aiPage,aiNav);
        promptsNav.Click+=(_,_)=>ShowPage(promptsPage,promptsNav);

        var languageLabel=FieldLabel(general,T("UiLanguage"));
        languageLabel.Margin=new Thickness(0,0,0,6);
        var language=new ComboBox {
            Name="LanguageSelector",SelectedValuePath=nameof(LanguageOption.Value),
            ItemTemplate=(DataTemplate)FindResource("LanguageOptionTemplate")
        };
        language.SetResourceReference(FrameworkElement.StyleProperty,"ComboBoxStyle");
        System.Windows.Automation.AutomationProperties.SetLabeledBy(language,languageLabel);
        TextSearch.SetTextPath(language,nameof(LanguageOption.Label));
        foreach(var (value,label) in new[]{(UiLanguage.System,T("SystemDefault")),(UiLanguage.Chinese,"简体中文"),
                                         (UiLanguage.Japanese,"日本語"),(UiLanguage.English,"English")}) {
            var culture=UiText.Resolve(value,UiText.Culture);
            language.Items.Add(new LanguageOption(value,label,UiTypography.ForLanguage(culture),
                System.Windows.Markup.XmlLanguage.GetLanguage(culture.IetfLanguageTag)));
        }
        language.SelectedValue=settings.Language;
        if(language.SelectedIndex<0) language.SelectedIndex=0;
        general.Children.Add(language);

        Section(general,T("Startup"));
        var startupRegistration=new StartupRegistration();
        var launchAtLogin=new CheckBox { Name="LaunchAtLogin",Content=T("LaunchAtLogin") };
        bool startupWasEnabled=false;
        try {
            startupWasEnabled=startupRegistration.IsEnabled();
            launchAtLogin.IsChecked=startupWasEnabled;
        } catch(Exception ex) when(ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) {
            launchAtLogin.IsEnabled=false;
            launchAtLogin.ToolTip=T("StartupReadFailed");
            ToolTipService.SetShowOnDisabled(launchAtLogin,true);
        }
        general.Children.Add(launchAtLogin);

        Section(general,T("Appearance"));
        FieldLabel(general,T("Theme"));
        var theme=new ComboBox { Name="ThemeSelector" }; theme.SetResourceReference(FrameworkElement.StyleProperty,"ComboBoxStyle");
        foreach(var item in new[]{ (AppTheme.System,T("SystemDefault")),(AppTheme.Light,T("Light")),(AppTheme.Dark,T("Dark")) }) {
            var option=new ComboBoxItem { Content=item.Item2,Tag=item.Item1 }; theme.Items.Add(option); if(item.Item1==settings.Theme) theme.SelectedItem=option;
        }
        general.Children.Add(theme);
        var themeHelp=Text(T("ThemeHelp"),12,true); themeHelp.Margin=new Thickness(0,6,0,0); general.Children.Add(themeHelp);
        var opacityLabel=FieldLabel(general,T("ToolbarOpacity"));
        var toolbarOpacity=new ComboBox { Name="ToolbarOpacitySelector" };
        toolbarOpacity.SetResourceReference(FrameworkElement.StyleProperty,"ComboBoxStyle");
        System.Windows.Automation.AutomationProperties.SetLabeledBy(toolbarOpacity,opacityLabel);
        var opacityOptions=Enumerable.Range(2,19).Select(step=>step*5).ToList();
        foreach(var percent in opacityOptions)
            toolbarOpacity.Items.Add(new ComboBoxItem { Content=$"{percent}%",Tag=percent });
        var savedOpacity=ToolbarWindow.NormalizeIdleOpacity(settings.ToolbarIdleOpacity);
        var nearestOpacity=opacityOptions.MinBy(percent=>Math.Abs(percent-savedOpacity));
        toolbarOpacity.SelectedItem=toolbarOpacity.Items.Cast<ComboBoxItem>().First(item=>Equals(item.Tag,nearestOpacity));
        general.Children.Add(toolbarOpacity);
        var opacityHelp=Text(T("ToolbarOpacityHelp"),12,true); opacityHelp.Margin=new Thickness(0,6,0,0); general.Children.Add(opacityHelp);

        Section(general,T("SelectionBehavior"));
        FieldLabel(general,T("ToolbarPosition"));
        var toolbarPosition=new ComboBox { Name="ToolbarPositionSelector" };
        toolbarPosition.SetResourceReference(FrameworkElement.StyleProperty,"ComboBoxStyle");
        foreach(var item in new[]{(ToolbarPlacement.Above,T("Above")),(ToolbarPlacement.Below,T("Below"))})
            toolbarPosition.Items.Add(new ComboBoxItem { Content=item.Item2,Tag=item.Item1 });
        toolbarPosition.SelectedIndex=settings.ToolbarPosition==ToolbarPlacement.Below?1:0;
        general.Children.Add(toolbarPosition);
        var positionHelp=Text(T("ToolbarPositionHelp"),12,true); positionHelp.Margin=new Thickness(0,6,0,6); general.Children.Add(positionHelp);
        var enabled=new CheckBox { Content=T("AutoShowToolbar"),IsChecked=settings.Enabled }; general.Children.Add(enabled);
        var terminal=new CheckBox { Content=T("TerminalShift"),IsChecked=settings.TerminalShift }; general.Children.Add(terminal);
        Section(general,T("AiResultWindow"));
        var autoCloseResult=new CheckBox {
            Name="AutoCloseResultOnDeactivate",
            Content=T("AutoCloseResult"),
            IsChecked=settings.AutoCloseResultOnDeactivate
        };
        var typography=new Grid();
        typography.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
        typography.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(108) });
        general.Children.Add(typography);
        var fontField=new StackPanel { Margin=new Thickness(0,0,12,0) };
        typography.Children.Add(fontField);
        var sizeField=new StackPanel();
        Grid.SetColumn(sizeField,1);
        typography.Children.Add(sizeField);

        var fontLanguage=UiText.MarkupLanguage;
        var fontOptions=Fonts.SystemFontFamilies
            .Select(font=>new KeyValuePair<string,string>(font.Source,font.FamilyNames.TryGetValue(fontLanguage,out var name)?name:font.Source))
            .OrderBy(option=>option.Value,StringComparer.Create(UiText.Culture,ignoreCase:true))
            .ToList();
        fontOptions.Insert(0,new("",T("DefaultFont")));
        var savedFont=settings.ResultFontFamily??"";
        var selectedFont=fontOptions.FindIndex(option=>string.Equals(option.Key,savedFont,StringComparison.OrdinalIgnoreCase));
        if(selectedFont<0) {
            selectedFont=1;
            fontOptions.Insert(selectedFont,new(savedFont,T("FontNotInstalled",savedFont)));
        }

        var fontLabel=FieldLabel(fontField,T("Font"));
        var resultFont=new ComboBox {
            Name="ResultFontSelector",ItemsSource=fontOptions,DisplayMemberPath="Value",SelectedValuePath="Key",
            SelectedIndex=selectedFont,MaxDropDownHeight=300
        };
        resultFont.SetResourceReference(FrameworkElement.StyleProperty,"ComboBoxStyle");
        System.Windows.Automation.AutomationProperties.SetLabeledBy(resultFont,fontLabel);
        fontField.Children.Add(resultFont);

        var sizeLabel=FieldLabel(sizeField,T("FontSize"));
        var resultSize=new ComboBox {
            Name="ResultFontSizeSelector",MaxDropDownHeight=300,
            ItemsSource=Enumerable.Range(ResultTypography.MinSize,ResultTypography.MaxSize-ResultTypography.MinSize+1),
            SelectedItem=ResultTypography.NormalizeSize(settings.ResultFontSize)
        };
        resultSize.SetResourceReference(FrameworkElement.StyleProperty,"ComboBoxStyle");
        System.Windows.Automation.AutomationProperties.SetLabeledBy(resultSize,sizeLabel);
        sizeField.Children.Add(resultSize);

        var fontPreview=Text(T("FontPreview"));
        fontPreview.Margin=new Thickness(0,8,0,0);
        general.Children.Add(fontPreview);
        int SelectedResultSize()=>resultSize.SelectedItem is int size?size:ResultTypography.DefaultSize;
        void UpdateFontPreview()
        {
            fontPreview.FontFamily=ResultTypography.ResolveFont(resultFont.SelectedValue as string);
            fontPreview.FontSize=SelectedResultSize();
        }
        resultFont.SelectionChanged+=(_,_)=>UpdateFontPreview();
        resultSize.SelectionChanged+=(_,_)=>UpdateFontPreview();
        UpdateFontPreview();
        general.Children.Add(autoCloseResult);

        Section(general,T("SmartButtons"));
        var currencyEnabled=new CheckBox { Name="CurrencyConversionEnabled",Content=T("SmartCurrencyEnabled"),IsChecked=settings.CurrencyConversion };
        general.Children.Add(currencyEnabled);
        var targetLabel=FieldLabel(general,T("SmartCurrencyTarget"));
        var currencyTarget=new ComboBox { Name="CurrencyTargetSelector",ItemsSource=new[]{"CNY","JPY","USD"},SelectedItem=settings.CurrencyTarget };
        currencyTarget.SetResourceReference(FrameworkElement.StyleProperty,"ComboBoxStyle");
        System.Windows.Automation.AutomationProperties.SetLabeledBy(currencyTarget,targetLabel);
        if(currencyTarget.SelectedIndex<0) currencyTarget.SelectedIndex=0;
        general.Children.Add(currencyTarget);
        var sourceLabel=FieldLabel(general,T("SmartCurrencySource"));
        var currencySource=new ComboBox { Name="CurrencySourceSelector",ItemsSource=MoneyParser.Currencies,SelectedItem=MoneyParser.Normalize(settings.CurrencySource) };
        currencySource.SetResourceReference(FrameworkElement.StyleProperty,"ComboBoxStyle");
        System.Windows.Automation.AutomationProperties.SetLabeledBy(currencySource,sourceLabel);
        general.Children.Add(currencySource);
        general.Children.Add(Text(T("SmartCurrencySettingsHelp"),12,true));
        var aiSmartAnalysis=new CheckBox { Name="AiSmartAnalysisEnabled",Content=T("SmartAiAutomatic"),IsChecked=settings.AiSmartAnalysis,Margin=new Thickness(0,10,0,0) };
        general.Children.Add(aiSmartAnalysis);
        general.Children.Add(Text(T("SmartAiAutomaticHelp"),12,true));

        Section(general,T("SearchAndExclusions"));
        var search=Field(general,T("SearchUrl"),settings.SearchUrl,T("SearchUrlHelp"));
        var excluded=Field(general,T("ExcludedProcesses"),settings.ExcludedApps,T("ExcludedProcessesHelp"));
        var shortcut=new Border { CornerRadius=new CornerRadius(7),Padding=new Thickness(12,10,12,10),Margin=new Thickness(0,24,0,4) };
        shortcut.SetResourceReference(Border.BackgroundProperty,"SurfaceBrush");
        shortcut.SetResourceReference(Border.BorderBrushProperty,"BorderBrush"); shortcut.BorderThickness=new Thickness(1);
        shortcut.Child=Text(T("ManualShortcut"),12,true); general.Children.Add(shortcut);

        var endpointSection=Section(ai,T("EndpointSettings")); endpointSection.Margin=new Thickness(0,0,0,4);
        var url=Field(ai,T("ApiBaseUrl"),settings.BaseUrl,T("ApiBaseUrlHelp"));
        var model=Field(ai,T("ModelId"),settings.Model,T("ModelIdHelp"));
        var thinking=new CheckBox {
            Name="DeepSeekFlashThinking",Content=T("DeepSeekThinking"),IsChecked=settings.DeepSeekFlashThinking,
            Margin=new Thickness(0,8,0,0),ToolTip=T("DeepSeekThinkingHelp")
        };
        ai.Children.Add(thinking);
        void UpdateThinkingOption()=>thinking.Visibility=AiClient.IsOfficialDeepSeekFlash(url.Text,model.Text)?Visibility.Visible:Visibility.Collapsed;
        url.TextChanged+=(_,_)=>UpdateThinkingOption();
        model.TextChanged+=(_,_)=>UpdateThinkingOption();
        UpdateThinkingOption();
        Section(ai,T("Authentication"));
        FieldLabel(ai,T("ApiKey"));
        var secretHost=new Grid(); ai.Children.Add(secretHost);
        var secret=new PasswordBox { Name="ApiKeyInput" }; secret.SetResourceReference(FrameworkElement.StyleProperty,"InputPasswordBoxStyle"); secretHost.Children.Add(secret);
        // Display a status overlay, never a fake password that could be saved as a key.
        var savedKeyMask=Text("●●●●●●●●",12,true); savedKeyMask.Name="SavedApiKeyMask";
        savedKeyMask.Margin=new Thickness(11,0,0,0); savedKeyMask.VerticalAlignment=VerticalAlignment.Center; savedKeyMask.IsHitTestVisible=false;
        secretHost.Children.Add(savedKeyMask);
        var secretHelp=Text(T("ApiKeyHelp"),12,true); secretHelp.Margin=new Thickness(0,6,0,0); ai.Children.Add(secretHelp);
        var clear=new CheckBox { Name="ClearSavedApiKey",Content=T("ClearSavedApiKey"),Margin=new Thickness(0,10,0,0) }; ai.Children.Add(clear);
        void UpdateKeyMask()=>savedKeyMask.Visibility=!string.IsNullOrEmpty(settings.EncryptedKey) && clear.IsChecked!=true && secret.Password.Length==0?Visibility.Visible:Visibility.Collapsed;
        secret.PasswordChanged+=(_,_)=>UpdateKeyMask();
        clear.Checked+=(_,_)=>UpdateKeyMask(); clear.Unchecked+=(_,_)=>UpdateKeyMask();
        UpdateKeyMask();
        var privacy=Text(T("AiPrivacy"),12,true); privacy.Margin=new Thickness(0,22,0,4); ai.Children.Add(privacy);

        var promptRows=new Grid();
        promptRows.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        promptRows.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
        promptsPage.Children.Add(promptRows);
        var promptHeading=new StackPanel { Margin=new Thickness(0,0,0,20) }; promptRows.Children.Add(promptHeading);
        promptHeading.Children.Add(Text(T("Prompts"),24,false,FontWeights.SemiBold));
        var promptSummary=Text(T("PromptsDescription"),12,true); promptSummary.Margin=new Thickness(0,6,0,0); promptHeading.Children.Add(promptSummary);
        var editorGrid=new Grid(); Grid.SetRow(editorGrid,1); promptRows.Children.Add(editorGrid);
        editorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(190) });
        editorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
        var listPanel=new DockPanel { Margin=new Thickness(0,0,16,0) }; editorGrid.Children.Add(listPanel);
        var drafts=new System.Collections.ObjectModel.ObservableCollection<PromptAction>(settings.Prompts.Select(item=>item.Copy()));
        foreach(var draft in drafts.Where(item=>item.IconKind==PromptIconKind.NerdFont && !string.IsNullOrWhiteSpace(item.IconFont) && !PromptIcons.IsNerdFontFamily(item.IconFont)))
            draft.IconFont="";
        var promptList=new PromptOrderList(drafts) { Name="PromptList",BorderThickness=new Thickness(1),Padding=new Thickness(4),Tag=T("DragToReorder") };
        promptList.SetResourceReference(ItemsControl.ItemTemplateProperty,"PromptOrderItemTemplate");
        promptList.SetResourceReference(ListBox.BackgroundProperty,"SurfaceBrush");
        promptList.SetResourceReference(ListBox.ForegroundProperty,"TextBrush");
        promptList.SetResourceReference(ListBox.BorderBrushProperty,"BorderBrush");
        var empty=new TextBlock { Name="PromptEmptyState",Text=T("NoPrompts"),TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap };
        empty.SetResourceReference(TextBlock.ForegroundProperty,"MutedTextBrush");
        var editorBorder=new Border { CornerRadius=new CornerRadius(8),BorderThickness=new Thickness(1),Padding=new Thickness(20) }; Grid.SetColumn(editorBorder,1); editorGrid.Children.Add(editorBorder);
        editorBorder.SetResourceReference(Border.BackgroundProperty,"SurfaceBrush");
        editorBorder.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");
        var editor=new Grid(); editorBorder.Child=new ScrollViewer { Content=editor,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };
        editor.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        editor.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        editor.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
        editor.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        var nameLabel=Text(T("ButtonName"),13,false,FontWeights.SemiBold); nameLabel.Margin=new Thickness(0,0,0,6); editor.Children.Add(nameLabel);
        var nameInput=Ui.Input(""); nameInput.Name="PromptNameInput"; Grid.SetRow(nameInput,1); editor.Children.Add(nameInput);
        var promptEditor=new StackPanel { Margin=new Thickness(0,14,0,0) }; Grid.SetRow(promptEditor,2); editor.Children.Add(promptEditor);
        FieldLabel(promptEditor,T("ToolbarIcon"));
        var iconKind=new ComboBox { Name="PromptIconKind" };
        iconKind.SetResourceReference(FrameworkElement.StyleProperty,"ComboBoxStyle");
        foreach(var option in new[]{(PromptIconKind.Emoji,"Emoji"),(PromptIconKind.NerdFont,"Nerd Font")})
            iconKind.Items.Add(new ComboBoxItem { Content=option.Item2,Tag=option.Item1 });
        var iconHeader=new DockPanel { LastChildFill=true };
        promptEditor.Children.Add(iconHeader);
        var preview=new ContentControl { Name="PromptIconPreview",Width=40,Height=36,Margin=new Thickness(0,0,10,0),ToolTip=T("ToolbarIconPreview") };
        DockPanel.SetDock(preview,Dock.Left); iconHeader.Children.Add(preview);
        iconHeader.Children.Add(iconKind);
        var iconOptions=new StackPanel(); promptEditor.Children.Add(iconOptions);
        var iconEntry=new DockPanel { Margin=new Thickness(0,8,0,0),LastChildFill=true }; iconOptions.Children.Add(iconEntry);
        var iconInput=Ui.Input(""); iconInput.Name="PromptIconInput"; iconInput.Height=36; iconInput.Padding=new Thickness(8,4,8,4);
        iconInput.ToolTip=T("PasteIconTooltip");
        System.Windows.Automation.AutomationProperties.SetName(iconInput,T("PasteIconName"));
        var iconHint=Text("",11,true); iconHint.Margin=new Thickness(0,5,0,0); iconOptions.Children.Add(iconHint);
        var fontPanel=new StackPanel { Margin=new Thickness(0,6,0,0) };
        var advanced=new Expander { Name="PromptIconAdvanced",Header=T("IconFontOptions"),Content=fontPanel,Margin=new Thickness(0,8,0,0),FontSize=12 };
        advanced.SetResourceReference(Control.ForegroundProperty,"MutedTextBrush"); iconOptions.Children.Add(advanced);
        var iconFont=new ComboBox { Name="PromptIconFont" };
        iconFont.SetResourceReference(FrameworkElement.StyleProperty,"ComboBoxStyle"); fontPanel.Children.Add(iconFont);
        var iconWarning=Text("",11,true); iconWarning.Name="PromptIconWarning"; iconWarning.Margin=new Thickness(0,6,0,0); promptEditor.Children.Add(iconWarning);
        var promptLabel=Text(T("PromptBody"),13,false,FontWeights.SemiBold); promptLabel.Margin=new Thickness(0,18,0,0); promptEditor.Children.Add(promptLabel);
        var contentInput=Ui.Input("",true); contentInput.Name="PromptContentInput"; contentInput.Height=180; contentInput.VerticalScrollBarVisibility=ScrollBarVisibility.Auto; contentInput.Margin=new Thickness(0,6,0,0); promptEditor.Children.Add(contentInput);
        var placeholder=Text(T("MissingPlaceholder"),12,true); placeholder.Margin=new Thickness(0,6,0,0); promptEditor.Children.Add(placeholder);
        var loadingEditor=false;
        PromptAction? Current()=>promptList.SelectedItem as PromptAction;
        var chooseIcon=Ui.Button(T("ChooseIcon"),()=> {
            if(Current() is not { } current) return;
            var picker=new NerdIconPicker(current.IconFont) { Owner=this };
            if(picker.ShowDialog()==true) iconInput.Text=picker.SelectedGlyph!;
        });
        chooseIcon.Name="ChooseNerdIcon"; chooseIcon.Margin=new Thickness(8,0,0,0); DockPanel.SetDock(chooseIcon,Dock.Right); iconEntry.Children.Add(chooseIcon);
        iconEntry.Children.Add(iconInput);
        void UpdateIconPreview()
        {
            var current=Current()??new PromptAction();
            advanced.Visibility=chooseIcon.Visibility=current.IconKind==PromptIconKind.NerdFont?Visibility.Visible:Visibility.Collapsed;
            iconHint.Text=current.IconKind==PromptIconKind.Emoji?T("EmojiHint"):T("NerdIconHint");
            preview.Content=new Viewbox { Width=22,Height=22,Child=PromptIcons.Create(current,out var warning) };
            var inputFont=current.IconKind==PromptIconKind.NerdFont
                ? (string.IsNullOrWhiteSpace(current.IconFont)?PromptIcons.NerdFonts.FirstOrDefault():current.IconFont)
                : null;
            iconInput.FontFamily=current.IconKind==PromptIconKind.Emoji?UiTypography.Emoji:
                inputFont==null?FontFamily:new FontFamily($"{inputFont}, {FontFamily.Source}");
            iconWarning.Text=warning;
            iconWarning.Visibility=warning.Length==0?Visibility.Collapsed:Visibility.Visible;
        }
        var add=Ui.Button(T("AddPrompt"),()=>{});
        add.Name="AddPromptButton"; add.Margin=new Thickness(0,10,0,0); DockPanel.SetDock(add,Dock.Bottom); listPanel.Children.Add(add);
        listPanel.Children.Add(promptList);
        var delete=Ui.Button(T("DeletePrompt"),()=>{},"DangerButtonStyle"); delete.Name="DeletePromptButton"; delete.HorizontalAlignment=HorizontalAlignment.Right; Grid.SetRow(delete,3); editor.Children.Add(delete);
        void UpdateEditor()
        {
            loadingEditor=true;
            advanced.IsExpanded=false;
            var current=Current();
            editorBorder.Visibility=current==null?Visibility.Collapsed:Visibility.Visible;
            empty.Visibility=current==null?Visibility.Visible:Visibility.Collapsed;
            delete.IsEnabled=current!=null;
            nameInput.Text=current?.Name??""; contentInput.Text=current?.Prompt??"";
            iconKind.SelectedItem=iconKind.Items.Cast<ComboBoxItem>().FirstOrDefault(item=>Equals(item.Tag,current?.IconKind??PromptIconKind.Emoji))??iconKind.Items[0];
            iconInput.Text=current?.Icon??"";
            var fontNames=new[]{T("AutomaticFont")}.Concat(PromptIcons.NerdFonts).ToList();
            iconFont.ItemsSource=fontNames; iconFont.SelectedItem=string.IsNullOrWhiteSpace(current?.IconFont)?T("AutomaticFont"):current.IconFont;
            UpdateIconPreview();
            loadingEditor=false;
        }
        Grid.SetColumn(empty,1); editorGrid.Children.Add(empty);
        add.Click+=(_,_)=>{
            if(drafts.Count>=12) return;
            var item=new PromptAction(); drafts.Add(item); promptList.SelectedItem=item; promptList.ScrollIntoView(item); add.IsEnabled=drafts.Count<12; nameInput.Focus();
        };
        delete.Click+=(_,_)=>{
            var index=promptList.SelectedIndex; if(index<0) return;
            drafts.RemoveAt(index);
            promptList.SelectedIndex=drafts.Count==0?-1:Math.Min(index,drafts.Count-1);
            add.IsEnabled=true; UpdateEditor();
        };
        promptList.SelectionChanged+=(_,_)=>UpdateEditor();
        nameInput.TextChanged+=(_,_)=> { if(!loadingEditor&&Current()!=null) { Current()!.Name=nameInput.Text; UpdateIconPreview(); } };
        contentInput.TextChanged+=(_,_)=> { if(!loadingEditor&&Current()!=null) Current()!.Prompt=contentInput.Text; };
        iconKind.SelectionChanged+=(_,_)=> { if(!loadingEditor&&Current()!=null) { Current()!.IconKind=(PromptIconKind)((ComboBoxItem)iconKind.SelectedItem).Tag; UpdateIconPreview(); } };
        iconInput.TextChanged+=(_,_)=> { if(!loadingEditor&&Current()!=null) { Current()!.Icon=iconInput.Text; UpdateIconPreview(); } };
        iconFont.SelectionChanged+=(_,_)=> { if(!loadingEditor&&Current()!=null) { Current()!.IconFont=iconFont.SelectedIndex<=0?"":iconFont.SelectedItem as string??""; UpdateIconPreview(); } };
        if(drafts.Count>0) promptList.SelectedIndex=0;
        add.IsEnabled=drafts.Count<12; UpdateEditor();

        var footer=new Border { BorderThickness=new Thickness(0,1,0,0),Padding=new Thickness(24,12,24,12) }; Grid.SetRow(footer,1); root.Children.Add(footer);
        footer.SetResourceReference(Border.BackgroundProperty,"SurfaceBrush");
        footer.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");
        var footerButtons=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right }; footer.Child=footerButtons;
        footerButtons.Children.Add(Ui.Button(T("Cancel"),Close));
        footerButtons.Children.Add(Ui.Button(T("Save"),()=>{
            try {
                AiClient.Endpoint(url.Text);
                Actions.SearchUri(search.Text,"test");
                if(drafts.Any(x=>string.IsNullOrWhiteSpace(x.Name)||string.IsNullOrWhiteSpace(x.Prompt))) throw new InvalidOperationException(T("PromptRequired"));
                if(drafts.Any(x=>PromptIcons.Glyph(x).Length==0)) throw new InvalidOperationException(T("IconRequired"));
                var selectedTheme=(theme.SelectedItem as ComboBoxItem)?.Tag is AppTheme value?value:AppTheme.System;
                var selectedOpacity=(toolbarOpacity.SelectedItem as ComboBoxItem)?.Tag is int percent?percent:ToolbarWindow.DefaultIdleOpacityPercent;
                var next=new Settings { Theme=selectedTheme,ToolbarPosition=toolbarPosition.SelectedIndex==1?ToolbarPlacement.Below:ToolbarPlacement.Above,ToolbarIdleOpacity=selectedOpacity,Enabled=enabled.IsChecked==true,TerminalShift=terminal.IsChecked==true,SearchUrl=search.Text.Trim(),ExcludedApps=excluded.Text,BaseUrl=url.Text.Trim(),Model=model.Text.Trim(),EncryptedKey=settings.EncryptedKey,Prompts=drafts.Select(x=> { var copy=x.Copy(); copy.Name=copy.Name.Trim(); return copy; }).ToList() };
                next.Language=language.SelectedValue is UiLanguage preference?preference:UiLanguage.System;
                next.AutoCloseResultOnDeactivate=autoCloseResult.IsChecked==true;
                next.ResultFontFamily=resultFont.SelectedValue as string??"";
                next.ResultFontSize=SelectedResultSize();
                next.DeepSeekFlashThinking=thinking.IsChecked==true;
                next.CurrencyConversion=currencyEnabled.IsChecked==true;
                next.AiSmartAnalysis=aiSmartAnalysis.IsChecked==true;
                next.CurrencyTarget=currencyTarget.SelectedItem as string??"CNY";
                next.CurrencySource=currencySource.SelectedItem as string??"CNY";
                if(clear.IsChecked==true) next.SetKey("");
                if(secret.Password.Length>0) next.SetKey(secret.Password);
                next.Save(); saved(next);
                var startupEnabled=launchAtLogin.IsChecked==true;
                if(launchAtLogin.IsEnabled && startupEnabled!=startupWasEnabled) {
                    try { startupRegistration.SetEnabled(startupEnabled,Environment.ProcessPath); }
                    catch(Exception ex) when(ex is System.Security.SecurityException or UnauthorizedAccessException or IOException or InvalidOperationException) {
                        MessageBox.Show(this,T("StartupWriteFailed",ex.Message),T("StartupWriteFailedTitle"),MessageBoxButton.OK,MessageBoxImage.Warning);
                    }
                }
                Close();
            } catch(Exception ex) { MessageBox.Show(this,ex.Message,T("SettingsSaveFailedTitle"),MessageBoxButton.OK,MessageBoxImage.Warning); }
        },"PrimaryButtonStyle"));
        ShowPage(generalPage,generalNav);
    }
}
