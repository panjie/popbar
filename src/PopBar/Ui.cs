namespace PopBar;

internal static class Ui
{
    internal static readonly Brush Background=new SolidColorBrush(Color.FromRgb(24,28,36));
    internal static readonly Brush Surface=new SolidColorBrush(Color.FromRgb(37,43,55));
    internal static readonly Brush Text=new SolidColorBrush(Color.FromRgb(232,237,246));
    internal static readonly Brush Muted=new SolidColorBrush(Color.FromRgb(155,168,188));
    internal static readonly Brush Accent=new SolidColorBrush(Color.FromRgb(133,209,181));
    internal static void Style(Window window) { window.Background=Background; window.Foreground=Text; window.FontFamily=new FontFamily("Microsoft YaHei UI"); window.FontSize=13; }
    internal static Button Button(string label,Action action)
    {
        var b=new Button { Content=label,Padding=new Thickness(13,8,13,8),Margin=new Thickness(3),Background=Surface,Foreground=Text,BorderThickness=new Thickness(0),Cursor=System.Windows.Input.Cursors.Hand,MinHeight=34 };
        b.Click+=(_,_)=>action(); return b;
    }
    internal static TextBlock Label(string text,bool muted=false) => new() { Text=text,Foreground=muted?Muted:Text,Margin=new Thickness(3,8,3,5),TextWrapping=TextWrapping.Wrap };
    internal static TextBox Input(string text,bool multiline=false) => new() { Text=text,Background=Surface,Foreground=Text,CaretBrush=Text,BorderBrush=Muted,Padding=new Thickness(8),Margin=new Thickness(3),AcceptsReturn=multiline,TextWrapping=multiline?TextWrapping.Wrap:TextWrapping.NoWrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MinHeight=multiline?78:34 };
}
