using System.Globalization;
using System.Resources;
using System.Runtime.InteropServices;
using System.Windows.Markup;

namespace PopBar;

public enum UiLanguage { System, Chinese, Japanese, English }

internal static class UiText
{
    static readonly ResourceManager strings=new("PopBar.Resources.Strings",typeof(UiText).Assembly);
    static readonly CultureInfo[] supportedCultures={CultureInfo.GetCultureInfo("en-US"),CultureInfo.GetCultureInfo("ja-JP"),CultureInfo.GetCultureInfo("zh-CN")};
    internal static CultureInfo Culture { get; private set; }=Resolve(UiLanguage.System,WindowsDisplayCulture());
    internal static XmlLanguage MarkupLanguage=>XmlLanguage.GetLanguage(Culture.IetfLanguageTag);

    [DllImport("kernel32.dll",ExactSpelling=true)]
    static extern ushort GetUserDefaultUILanguage();

    static CultureInfo WindowsDisplayCulture()
    {
        try { return CultureInfo.GetCultureInfo(GetUserDefaultUILanguage()); }
        catch(CultureNotFoundException) { return CultureInfo.CurrentUICulture; }
    }

    internal static CultureInfo Resolve(UiLanguage preference,CultureInfo windowsCulture)
    {
        return CultureInfo.GetCultureInfo(preference switch {
            UiLanguage.Chinese=>"zh-CN",
            UiLanguage.Japanese=>"ja-JP",
            UiLanguage.English=>"en-US",
            _=>windowsCulture.TwoLetterISOLanguageName switch { "zh"=>"zh-CN","ja"=>"ja-JP",_=>"en-US" }
        });
    }

    internal static bool Apply(UiLanguage preference)
    {
        var culture=Resolve(preference,WindowsDisplayCulture());
        if(Culture.Equals(culture)) return false;
        Culture=culture;
        return true;
    }

    internal static string T(string key) => strings.GetString(key,Culture) ?? key;
    internal static string T(string key,params object[] args) => string.Format(Culture,T(key),args);

    internal static bool IsTranslation(string key,string text)
    {
        foreach(var culture in supportedCultures)
            if(strings.GetString(key,culture)==text) return true;
        return false;
    }
}
