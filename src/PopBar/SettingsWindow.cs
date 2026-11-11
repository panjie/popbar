namespace PopBar;

internal sealed class SettingsWindow:Window
{
    internal SettingsWindow(Settings settings,Action<Settings> saved)
    {
        Title="PopBar · 设置"; Width=820; Height=680; MinWidth=680; MinHeight=480; WindowStartupLocation=WindowStartupLocation.CenterScreen; Ui.Style(this);
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
        var subtitle=Text("划词，让下一步更简单",12,true); subtitle.Margin=new Thickness(0,5,0,0); brand.Children.Add(subtitle);
        sideContent.Children.Add(navigation);

        var pages=new Grid { Margin=new Thickness(30,24,30,18) }; Grid.SetColumn(pages,1); body.Children.Add(pages);
        (Grid Page,StackPanel Content) StandardPage(string name,string title,string description)
        {
            var page=new Grid { Name=name,Visibility=Visibility.Collapsed };
            var dock=new DockPanel(); page.Children.Add(dock);
            var heading=new StackPanel { Margin=new Thickness(0,0,0,20) }; DockPanel.SetDock(heading,Dock.Top); dock.Children.Add(heading);
            heading.Children.Add(Text(title,24,false,FontWeights.SemiBold));
            var summary=Text(description,12,true); summary.Margin=new Thickness(0,6,0,0); heading.Children.Add(summary);
            var content=new StackPanel();
            dock.Children.Add(new ScrollViewer { Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled });
            pages.Children.Add(page); return (page,content);
        }

        var (generalPage,general)=StandardPage("GeneralPage","常规","管理外观、划词行为和搜索方式。");
        var (aiPage,ai)=StandardPage("AiPage","AI 服务","连接 OpenAI 兼容的 Chat Completions 接口。");
        var promptsPage=new Grid { Name="PromptsPage",Visibility=Visibility.Collapsed }; pages.Children.Add(promptsPage);

        Button Navigation(string name,string glyph,string label)
        {
            var content=new StackPanel { Orientation=Orientation.Horizontal };
            content.Children.Add(new TextBlock { Text=glyph,FontFamily=new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),FontSize=16,VerticalAlignment=VerticalAlignment.Center });
            content.Children.Add(new TextBlock { Text=label,Margin=new Thickness(11,0,0,0),VerticalAlignment=VerticalAlignment.Center });
            var button=new Button { Name=name,Content=content }; button.SetResourceReference(FrameworkElement.StyleProperty,"NavigationButtonStyle"); navigation.Children.Add(button); return button;
        }
        var generalNav=Navigation("GeneralNavigationButton","\uE80F","常规");
        var aiNav=Navigation("AiNavigationButton","\uE73E","AI 服务");
        var promptsNav=Navigation("PromptsNavigationButton","\uE70F","提示词");
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

        var appearanceSection=Section(general,"外观"); appearanceSection.Margin=new Thickness(0,0,0,4);
        FieldLabel(general,"主题");
        var theme=new ComboBox { Name="ThemeSelector" }; theme.SetResourceReference(FrameworkElement.StyleProperty,"ComboBoxStyle");
        foreach(var item in new[]{ (AppTheme.System,"跟随系统"),(AppTheme.Light,"亮色"),(AppTheme.Dark,"暗色") }) {
            var option=new ComboBoxItem { Content=item.Item2,Tag=item.Item1 }; theme.Items.Add(option); if(item.Item1==settings.Theme) theme.SelectedItem=option;
        }
        general.Children.Add(theme);
        var themeHelp=Text("跟随系统会在 Windows 应用主题变化时自动更新。",12,true); themeHelp.Margin=new Thickness(0,6,0,0); general.Children.Add(themeHelp);

        Section(general,"划词行为");
        FieldLabel(general,"工具条显示位置");
        var toolbarPosition=new ComboBox { Name="ToolbarPositionSelector" };
        toolbarPosition.SetResourceReference(FrameworkElement.StyleProperty,"ComboBoxStyle");
        foreach(var item in new[]{(ToolbarPlacement.Above,"上方"),(ToolbarPlacement.Below,"下方")})
            toolbarPosition.Items.Add(new ComboBoxItem { Content=item.Item2,Tag=item.Item1 });
        toolbarPosition.SelectedIndex=settings.ToolbarPosition==ToolbarPlacement.Below?1:0;
        general.Children.Add(toolbarPosition);
        var positionHelp=Text("相对于划词结束时的鼠标位置；靠近屏幕边缘时自动避让。",12,true); positionHelp.Margin=new Thickness(0,6,0,6); general.Children.Add(positionHelp);
        var enabled=new CheckBox { Content="鼠标划词后自动弹出",IsChecked=settings.Enabled }; general.Children.Add(enabled);
        var terminal=new CheckBox { Content="终端中仅在按住 Shift 划词时弹出",IsChecked=settings.TerminalShift }; general.Children.Add(terminal);
        Section(general,"搜索与排除");
        var search=Field(general,"搜索地址",settings.SearchUrl,"使用 {text} 代表选中文字，例如 https://www.bing.com/search?q={text}");
        var excluded=Field(general,"排除的应用进程名",settings.ExcludedApps,"使用逗号分隔，不包含 .exe。密码管理器默认已排除。");
        var shortcut=new Border { CornerRadius=new CornerRadius(7),Padding=new Thickness(12,10,12,10),Margin=new Thickness(0,24,0,4) };
        shortcut.SetResourceReference(Border.BackgroundProperty,"SurfaceBrush");
        shortcut.SetResourceReference(Border.BorderBrushProperty,"BorderBrush"); shortcut.BorderThickness=new Thickness(1);
        shortcut.Child=Text("手动唤起：选中文字后按 Ctrl + Alt + Space",12,true); general.Children.Add(shortcut);

        var endpointSection=Section(ai,"接口配置"); endpointSection.Margin=new Thickness(0,0,0,4);
        var url=Field(ai,"API 基础地址",settings.BaseUrl,"例如 https://api.deepseek.com/v1，无需添加 /chat/completions。远程服务必须使用 HTTPS。");
        var model=Field(ai,"模型 ID",settings.Model,"填写服务商或本地服务提供的实际模型名称。");
        Section(ai,"身份验证");
        FieldLabel(ai,"API Key");
        var secret=new PasswordBox(); secret.SetResourceReference(FrameworkElement.StyleProperty,"InputPasswordBoxStyle"); ai.Children.Add(secret);
        var secretHelp=Text("留空保留已有密钥；密钥使用 Windows DPAPI 当前用户加密存储。",12,true); secretHelp.Margin=new Thickness(0,6,0,0); ai.Children.Add(secretHelp);
        var clear=new CheckBox { Content="清除已保存密钥（适用于无需密钥的本地服务）",Margin=new Thickness(0,10,0,0) }; ai.Children.Add(clear);
        var privacy=Text("只有点击 AI 按钮时才会发送选中文字。当前支持 OpenAI 兼容 Chat Completions 协议。",12,true); privacy.Margin=new Thickness(0,22,0,4); ai.Children.Add(privacy);

        var promptRows=new Grid();
        promptRows.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        promptRows.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
        promptsPage.Children.Add(promptRows);
        var promptHeading=new StackPanel { Margin=new Thickness(0,0,0,20) }; promptRows.Children.Add(promptHeading);
        promptHeading.Children.Add(Text("提示词",24,false,FontWeights.SemiBold));
        var promptSummary=Text("自定义工具条中的 AI 操作，最多 12 个。{{text}} 会被选中文字替换。",12,true); promptSummary.Margin=new Thickness(0,6,0,0); promptHeading.Children.Add(promptSummary);
        var editorGrid=new Grid(); Grid.SetRow(editorGrid,1); promptRows.Children.Add(editorGrid);
        editorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(190) });
        editorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
        var listPanel=new DockPanel { Margin=new Thickness(0,0,16,0) }; editorGrid.Children.Add(listPanel);
        var drafts=settings.Prompts.Select(item=>item.Copy()).ToList();
        var promptList=new ListBox { Name="PromptList",ItemsSource=drafts,DisplayMemberPath="Name",BorderThickness=new Thickness(1),Padding=new Thickness(4) };
        promptList.SetResourceReference(ListBox.BackgroundProperty,"SurfaceBrush");
        promptList.SetResourceReference(ListBox.ForegroundProperty,"TextBrush");
        promptList.SetResourceReference(ListBox.BorderBrushProperty,"BorderBrush");
        var empty=new TextBlock { Name="PromptEmptyState",Text="还没有提示词。\n点击“添加提示词”开始。",TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap };
        empty.SetResourceReference(TextBlock.ForegroundProperty,"MutedTextBrush");
        var editorBorder=new Border { CornerRadius=new CornerRadius(8),BorderThickness=new Thickness(1),Padding=new Thickness(20) }; Grid.SetColumn(editorBorder,1); editorGrid.Children.Add(editorBorder);
        editorBorder.SetResourceReference(Border.BackgroundProperty,"SurfaceBrush");
        editorBorder.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");
        var editor=new Grid(); editorBorder.Child=new ScrollViewer { Content=editor,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };
        editor.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        editor.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        editor.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
        editor.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        var nameLabel=Text("按钮名称",13,false,FontWeights.SemiBold); nameLabel.Margin=new Thickness(0,0,0,6); editor.Children.Add(nameLabel);
        var nameInput=Ui.Input(""); nameInput.Name="PromptNameInput"; Grid.SetRow(nameInput,1); editor.Children.Add(nameInput);
        var promptEditor=new StackPanel { Margin=new Thickness(0,14,0,0) }; Grid.SetRow(promptEditor,2); editor.Children.Add(promptEditor);
        FieldLabel(promptEditor,"工具栏图标");
        var iconKind=new ComboBox { Name="PromptIconKind" };
        iconKind.SetResourceReference(FrameworkElement.StyleProperty,"ComboBoxStyle");
        foreach(var option in new[]{(PromptIconKind.Default,"默认图标"),(PromptIconKind.Emoji,"Emoji"),(PromptIconKind.NerdFont,"Nerd Font")})
            iconKind.Items.Add(new ComboBoxItem { Content=option.Item2,Tag=option.Item1 });
        promptEditor.Children.Add(iconKind);
        var iconOptions=new StackPanel(); promptEditor.Children.Add(iconOptions);
        var iconInput=Field(iconOptions,"图标字符或码点","","Emoji 可用 Win + . 输入；Nerd Font 可粘贴字符或输入 U+F121。"); iconInput.Name="PromptIconInput";
        var fontPanel=new StackPanel(); iconOptions.Children.Add(fontPanel);
        FieldLabel(fontPanel,"已安装的 Nerd Font");
        var iconFont=new ComboBox { Name="PromptIconFont" };
        iconFont.SetResourceReference(FrameworkElement.StyleProperty,"ComboBoxStyle"); fontPanel.Children.Add(iconFont);
        var preview=new ContentControl { Name="PromptIconPreview",Width=34,Height=34,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,8,0,0) }; promptEditor.Children.Add(preview);
        var iconWarning=Text("",12,true); iconWarning.Name="PromptIconWarning"; promptEditor.Children.Add(iconWarning);
        promptEditor.Children.Add(Text("提示词内容",13,false,FontWeights.SemiBold));
        var contentInput=Ui.Input("",true); contentInput.Name="PromptContentInput"; contentInput.Height=180; contentInput.VerticalScrollBarVisibility=ScrollBarVisibility.Auto; contentInput.Margin=new Thickness(0,6,0,0); promptEditor.Children.Add(contentInput);
        var placeholder=Text("未填写 {{text}} 时，选中文字会附加到提示词末尾。",12,true); placeholder.Margin=new Thickness(0,6,0,0); promptEditor.Children.Add(placeholder);
        var loadingEditor=false;
        PromptAction? Current()=>promptList.SelectedItem as PromptAction;
        void UpdateIconPreview()
        {
            var current=Current()??new PromptAction();
            iconOptions.Visibility=current.IconKind==PromptIconKind.Default?Visibility.Collapsed:Visibility.Visible;
            fontPanel.Visibility=current.IconKind==PromptIconKind.NerdFont?Visibility.Visible:Visibility.Collapsed;
            preview.Content=new Viewbox { Width=22,Height=22,Child=PromptIcons.Create(current,out var warning) };
            iconWarning.Text=warning;
        }
        var add=Ui.Button("＋ 添加提示词",()=>{});
        add.Name="AddPromptButton"; add.Margin=new Thickness(0,10,0,0); DockPanel.SetDock(add,Dock.Bottom); listPanel.Children.Add(add);
        listPanel.Children.Add(promptList);
        var delete=Ui.Button("删除提示词",()=>{},"DangerButtonStyle"); delete.Name="DeletePromptButton"; delete.HorizontalAlignment=HorizontalAlignment.Right; Grid.SetRow(delete,3); editor.Children.Add(delete);
        void UpdateEditor()
        {
            loadingEditor=true;
            var current=Current();
            editorBorder.Visibility=current==null?Visibility.Collapsed:Visibility.Visible;
            empty.Visibility=current==null?Visibility.Visible:Visibility.Collapsed;
            delete.IsEnabled=current!=null;
            nameInput.Text=current?.Name??""; contentInput.Text=current?.Prompt??"";
            iconKind.SelectedItem=iconKind.Items.Cast<ComboBoxItem>().FirstOrDefault(item=>Equals(item.Tag,current?.IconKind??PromptIconKind.Default))??iconKind.Items[0];
            iconInput.Text=current?.Icon??"";
            var fontNames=new[]{"自动选择"}.Concat(PromptIcons.NerdFonts).ToList();
            if(!string.IsNullOrWhiteSpace(current?.IconFont) && !fontNames.Contains(current.IconFont)) fontNames.Add(current.IconFont);
            iconFont.ItemsSource=fontNames; iconFont.SelectedItem=string.IsNullOrWhiteSpace(current?.IconFont)?"自动选择":current.IconFont;
            UpdateIconPreview();
            loadingEditor=false;
        }
        Grid.SetColumn(empty,1); editorGrid.Children.Add(empty);
        add.Click+=(_,_)=>{
            if(drafts.Count>=12) return;
            var item=new PromptAction(); drafts.Add(item); promptList.Items.Refresh(); promptList.SelectedItem=item; add.IsEnabled=drafts.Count<12; nameInput.Focus();
        };
        delete.Click+=(_,_)=>{
            var index=promptList.SelectedIndex; if(index<0) return;
            drafts.RemoveAt(index); promptList.Items.Refresh();
            promptList.SelectedIndex=drafts.Count==0?-1:Math.Min(index,drafts.Count-1);
            add.IsEnabled=true; UpdateEditor();
        };
        promptList.SelectionChanged+=(_,_)=>UpdateEditor();
        nameInput.TextChanged+=(_,_)=> { if(!loadingEditor&&Current()!=null) { Current()!.Name=nameInput.Text; UpdateIconPreview(); } };
        nameInput.LostFocus+=(_,_)=>promptList.Items.Refresh();
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
        footerButtons.Children.Add(Ui.Button("取消",Close));
        footerButtons.Children.Add(Ui.Button("保存",()=>{
            try {
                AiClient.Endpoint(url.Text);
                Actions.SearchUri(search.Text,"test");
                if(drafts.Any(x=>string.IsNullOrWhiteSpace(x.Name)||string.IsNullOrWhiteSpace(x.Prompt))) throw new InvalidOperationException("提示词名称和内容不能为空。");
                if(drafts.Any(x=>x.IconKind!=PromptIconKind.Default && PromptIcons.Glyph(x).Length==0)) throw new InvalidOperationException("自定义图标必须填写一个 Emoji 或 Nerd Font 字符/有效码点。");
                var selectedTheme=(theme.SelectedItem as ComboBoxItem)?.Tag is AppTheme value?value:AppTheme.System;
                var next=new Settings { Theme=selectedTheme,ToolbarPosition=toolbarPosition.SelectedIndex==1?ToolbarPlacement.Below:ToolbarPlacement.Above,Enabled=enabled.IsChecked==true,TerminalShift=terminal.IsChecked==true,SearchUrl=search.Text.Trim(),ExcludedApps=excluded.Text,BaseUrl=url.Text.Trim(),Model=model.Text.Trim(),EncryptedKey=settings.EncryptedKey,Prompts=drafts.Select(x=> { var copy=x.Copy(); copy.Name=copy.Name.Trim(); return copy; }).ToList() };
                if(clear.IsChecked==true) next.SetKey("");
                if(secret.Password.Length>0) next.SetKey(secret.Password);
                next.Save(); saved(next); Close();
            } catch(Exception ex) { MessageBox.Show(this,ex.Message,"无法保存",MessageBoxButton.OK,MessageBoxImage.Warning); }
        },"PrimaryButtonStyle"));
        ShowPage(generalPage,generalNav);
    }
}
