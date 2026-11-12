namespace PopBar;

internal static class ResultTypography
{
    internal const int DefaultSize=13;
    internal const int MinSize=10;
    internal const int MaxSize=32;

    internal static FontFamily DefaultFont=>UiTypography.ForLanguage(UiText.Culture);
    internal static FontFamily DefaultCodeFont=>UiTypography.CodeForLanguage(UiText.Culture);

    internal static int NormalizeSize(int size) => size is >=MinSize and <=MaxSize ? size : DefaultSize;

    internal static FontFamily ResolveFont(string? source)
    {
        if(string.IsNullOrWhiteSpace(source)) return DefaultFont;
        return Fonts.SystemFontFamilies.FirstOrDefault(font=>string.Equals(font.Source,source,StringComparison.OrdinalIgnoreCase))
            ?? DefaultFont;
    }
}
