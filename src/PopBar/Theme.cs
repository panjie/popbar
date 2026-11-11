using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace PopBar;

internal static class ThemeManager
{
    static AppTheme current=AppTheme.Light;
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(nint window,int attribute,ref int value,int size);

    internal static bool ApplyTitleBar(Window window)
    {
        if(window.WindowStyle==WindowStyle.None) return true;
        var handle=new WindowInteropHelper(window).Handle;
        if(handle==0) return true;
        var dark=current==AppTheme.Dark?1:0;
        // Unsupported DWM attributes return HRESULT; keep the native frame on older Windows.
        if(DwmSetWindowAttribute(handle,20,ref dark,sizeof(int))<0)
            DwmSetWindowAttribute(handle,19,ref dark,sizeof(int));
        var caption=SetColor(35,"WindowBackgroundBrush");
        var text=SetColor(36,"TextBrush");
        SetColor(34,"BorderBrush");
        return caption>=0 && text>=0;
        int SetColor(int attribute,string key)
        {
            var color=((SolidColorBrush)Application.Current.FindResource(key)).Color;
            var value=color.R|(color.G<<8)|(color.B<<16);
            return DwmSetWindowAttribute(handle,attribute,ref value,sizeof(int));
        }
    }
    internal static AppTheme Resolve(AppTheme preference,bool systemUsesLight) => preference switch {
        AppTheme.System=>systemUsesLight?AppTheme.Light:AppTheme.Dark,
        AppTheme.Light=>AppTheme.Light,
        AppTheme.Dark=>AppTheme.Dark,
        _=>systemUsesLight?AppTheme.Light:AppTheme.Dark
    };
    internal static bool SystemUsesLightTheme()
    {
        try { return Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",null) is int value&&value!=0; }
        catch { return false; }
    }
    internal static void Apply(AppTheme preference)
    {
        var app=Application.Current ?? throw new InvalidOperationException("应用程序尚未初始化。");
        var resolved=Resolve(preference,SystemUsesLightTheme());
        var source=new Uri($"/PopBar;component/Themes/{resolved}.xaml",UriKind.Relative);
        var dictionaries=app.Resources.MergedDictionaries;
        var index=-1;
        for(var i=0;i<dictionaries.Count;i++) if(dictionaries[i].Source?.OriginalString.StartsWith("/PopBar;component/Themes/",StringComparison.OrdinalIgnoreCase)==true) { index=i; break; }
        current=resolved;
        if(index<0||dictionaries[index].Source!=source) {
            var theme=new ResourceDictionary { Source=source };
            if(index>=0) dictionaries[index]=theme; else dictionaries.Insert(0,theme);
        }
        foreach(Window window in app.Windows) ApplyTitleBar(window);
    }
}
