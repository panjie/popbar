namespace PopBar;

internal static class Ui
{
    internal static void Style(Window window)
    {
        window.SetResourceReference(Window.BackgroundProperty,"WindowBackgroundBrush");
        window.SetResourceReference(Window.ForegroundProperty,"TextBrush");
        window.FontSize=13;
        ApplyTextLanguage(window);
        window.Icon=System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/PopBar;component/Assets/PopBar.ico"));
        window.SourceInitialized+=(_,_)=>ThemeManager.ApplyTitleBar(window);
    }
    internal static void ApplyTextLanguage(Window window)
    {
        var font=UiTypography.ForLanguage(UiText.Culture);
        var language=UiText.MarkupLanguage;
        window.FontFamily=font;
        window.Language=language;
        // ToolTips open in a separate visual tree and need their own text defaults.
        var resources=Application.Current.Resources;
        if(!Equals(resources["UiFontFamily"],font)) resources["UiFontFamily"]=font;
        if(!Equals(resources["UiMarkupLanguage"],language)) resources["UiMarkupLanguage"]=language;
    }
    internal static Button Button(string label,Action action,string styleKey="SecondaryButtonStyle")
    {
        var b=new Button { Content=label,Cursor=System.Windows.Input.Cursors.Hand };
        b.SetResourceReference(FrameworkElement.StyleProperty,styleKey);
        b.Click+=(_,_)=>action(); return b;
    }
    internal static Button GlyphButton(string glyph,string name,Action action)
    {
        var b=Button("",action,"ToolbarButtonStyle");
        b.Content=new TextBlock { Text=glyph,FontFamily=UiTypography.Icons,FontSize=14 };
        b.ToolTip=name;
        System.Windows.Automation.AutomationProperties.SetName(b,name);
        return b;
    }
    internal static TextBlock Label(string text,bool muted=false)
    {
        var label=new TextBlock { Text=text,Margin=new Thickness(3,8,3,5),TextWrapping=TextWrapping.Wrap };
        label.SetResourceReference(TextBlock.ForegroundProperty,muted?"MutedTextBrush":"TextBrush");
        return label;
    }
    internal static TextBox Input(string text,bool multiline=false)
    {
        var box=new TextBox { Text=text,AcceptsReturn=multiline,TextWrapping=multiline?TextWrapping.Wrap:TextWrapping.NoWrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MinHeight=multiline?78:32,VerticalContentAlignment=multiline?VerticalAlignment.Top:VerticalAlignment.Center };
        box.SetResourceReference(FrameworkElement.StyleProperty,"InputTextBoxStyle");
        return box;
    }
}
