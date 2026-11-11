namespace PopBar;

internal static class ResultTypography
{
    internal const int DefaultSize=13;
    internal const int MinSize=10;
    internal const int MaxSize=32;

    internal static readonly FontFamily DefaultFont=new("Segoe UI Variable, Segoe UI, Microsoft YaHei UI");
    internal static readonly FontFamily DefaultCodeFont=new("Cascadia Code, Consolas, Microsoft YaHei UI");

    internal static int NormalizeSize(int size) => size is >=MinSize and <=MaxSize ? size : DefaultSize;

    internal static FontFamily ResolveFont(string? source)
    {
        if(string.IsNullOrWhiteSpace(source)) return DefaultFont;
        return Fonts.SystemFontFamilies.FirstOrDefault(font=>string.Equals(font.Source,source,StringComparison.OrdinalIgnoreCase))
            ?? DefaultFont;
    }
}
