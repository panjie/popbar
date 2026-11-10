namespace PopBar;

internal sealed class SettingsWindow:Window
{
    internal SettingsWindow(Settings settings,Action<Settings> saved)
    {
        Title="PopBar · 设置"; Width=650; Height=760; MinWidth=510; MinHeight=450; WindowStartupLocation=WindowStartupLocation.CenterScreen; Ui.Style(this);
        var root=new DockPanel { Margin=new Thickness(22) }; Content=root;
        var footer=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right }; DockPanel.SetDock(footer,Dock.Bottom); root.Children.Add(footer);
        var stack=new StackPanel(); root.Children.Add(new ScrollViewer { Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto });
        stack.Children.Add(new TextBlock { Text="PopBar",FontSize=30,FontWeight=FontWeights.SemiBold,Foreground=Ui.Accent });
        stack.Children.Add(Ui.Label("选中文字，让下一步更简单。  Ctrl + Alt + Space 手动唤起",true));
        var enabled=new CheckBox { Content="鼠标划词后自动弹出",IsChecked=settings.Enabled,Margin=new Thickness(3,12,3,8),Foreground=Ui.Text }; stack.Children.Add(enabled);
        var terminal=new CheckBox { Content="终端中仅在按住 Shift 划词时弹出",IsChecked=settings.TerminalShift,Margin=new Thickness(3,4,3,8),Foreground=Ui.Text }; stack.Children.Add(terminal);
        TextBox Field(string label,string value,bool multi=false) { stack.Children.Add(Ui.Label(label)); var box=Ui.Input(value,multi); stack.Children.Add(box); return box; }
        var search=Field("搜索地址（用 {text} 代表选中文字）",settings.SearchUrl);
        var excluded=Field("排除的应用进程名（逗号分隔，不含 .exe）",settings.ExcludedApps);
        stack.Children.Add(new Separator { Margin=new Thickness(3,18,3,10) });
        stack.Children.Add(new TextBlock { Text="AI 文本处理",FontSize=20,Foreground=Ui.Accent });
        stack.Children.Add(Ui.Label("仅点击 AI 按钮时发送选中文字。支持 OpenAI 兼容 Chat Completions 接口。",true));
        var url=Field("API 基础地址（如 https://api.deepseek.com/v1）",settings.BaseUrl);
        var model=Field("模型 ID（填写服务商提供的模型名称）",settings.Model);
        stack.Children.Add(Ui.Label("API Key（留空保留已有密钥）"));
        var secret=new PasswordBox { Background=Ui.Surface,Foreground=Ui.Text,Padding=new Thickness(8),Margin=new Thickness(3) }; stack.Children.Add(secret);
        var clear=new CheckBox { Content="清除已保存密钥（用于无需密钥的本地服务）",Foreground=Ui.Text,Margin=new Thickness(3,8,3,8) }; stack.Children.Add(clear);
        stack.Children.Add(Ui.Label("提示词按钮 · {{text}} 会被选中文字替换",true));
        var editors=new StackPanel(); stack.Children.Add(editors);
        var rows=new List<(StackPanel Panel,TextBox Name,TextBox Prompt)>();
        void Add(PromptAction action)
        {
            var panel=new StackPanel { Margin=new Thickness(0,5,0,10) }; var name=Ui.Input(action.Name); var prompt=Ui.Input(action.Prompt,true);
            panel.Children.Add(name); panel.Children.Add(prompt);
            panel.Children.Add(Ui.Button("删除按钮",()=> { editors.Children.Remove(panel); rows.RemoveAll(x=>x.Panel==panel); }));
            rows.Add((panel,name,prompt)); editors.Children.Add(panel);
        }
        foreach(var p in settings.Prompts) Add(p);
        stack.Children.Add(Ui.Button("＋ 添加提示词",()=> { if(rows.Count<12) Add(new()); }));
        footer.Children.Add(Ui.Button("取消",Close));
        footer.Children.Add(Ui.Button("保存",()=> {
            try {
                AiClient.Endpoint(url.Text);
                Actions.SearchUri(search.Text,"test");
                if(rows.Any(x=>string.IsNullOrWhiteSpace(x.Name.Text)||string.IsNullOrWhiteSpace(x.Prompt.Text))) throw new InvalidOperationException("提示词名称和内容不能为空。");
                var next=new Settings { Enabled=enabled.IsChecked==true,TerminalShift=terminal.IsChecked==true,SearchUrl=search.Text.Trim(),ExcludedApps=excluded.Text,BaseUrl=url.Text.Trim(),Model=model.Text.Trim(),EncryptedKey=settings.EncryptedKey,Prompts=rows.Select(x=>new PromptAction { Name=x.Name.Text.Trim(),Prompt=x.Prompt.Text }).ToList() };
                if(clear.IsChecked==true) next.SetKey("");
                if(secret.Password.Length>0) next.SetKey(secret.Password);
                next.Save(); saved(next); Close();
            } catch(Exception ex) { MessageBox.Show(this,ex.Message,"无法保存",MessageBoxButton.OK,MessageBoxImage.Warning); }
        }));
    }
}
