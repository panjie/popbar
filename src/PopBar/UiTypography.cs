using System.Globalization;

namespace PopBar;

internal static class UiTypography
{
    static readonly FontFamily japanese=new("Yu Gothic UI, Meiryo UI, Segoe UI");
    static readonly FontFamily chinese=new("Microsoft YaHei UI, Microsoft YaHei, Segoe UI");
    static readonly FontFamily english=new("Segoe UI Variable, Segoe UI");

    static readonly FontFamily japaneseCode=new($"Cascadia Code, Consolas, {japanese.Source}");
    static readonly FontFamily chineseCode=new($"Cascadia Code, Consolas, {chinese.Source}");
    static readonly FontFamily englishCode=new($"Cascadia Code, Consolas, {english.Source}");

    internal static readonly FontFamily Icons=new("Segoe Fluent Icons, Segoe MDL2 Assets");
    internal static readonly FontFamily Emoji=new("Segoe UI Emoji, Segoe UI Symbol");

    internal static FontFamily ForLanguage(CultureInfo culture) => culture.TwoLetterISOLanguageName switch {
        "ja"=>japanese,
        "zh"=>chinese,
        _=>english
    };

    internal static FontFamily CodeForLanguage(CultureInfo culture) => culture.TwoLetterISOLanguageName switch {
        "ja"=>japaneseCode,
        "zh"=>chineseCode,
        _=>englishCode
    };
}
