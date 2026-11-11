using System.Globalization;
using System.Text;

namespace PopBar;

internal static class PromptIcons
{
    internal static readonly string[] NerdFonts=Fonts.SystemFontFamilies.Select(font=>font.Source)
        .Where(IsNerdFontFamily).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name=>name).ToArray();

    internal static bool IsNerdFontFamily(string name)=>
        new[]{"JetBrainsMono NF","JetBrainsMono NFM","JetBrainsMono NFP",
            "JetBrainsMonoNL NF","JetBrainsMonoNL NFM","JetBrainsMonoNL NFP"}
        .Contains(name,StringComparer.OrdinalIgnoreCase);

    internal static string Glyph(PromptAction action)
    {
        var value=(action.Icon??"").Trim();
        if(action.IconKind==PromptIconKind.NerdFont) {
            if(value.StartsWith(@"\u",StringComparison.Ordinal)) {
                if(value.Length is not (6 or 12)) return "";
                var decoded=new StringBuilder();
                for(var offset=0;offset<value.Length;offset+=6) {
                    if(value.Substring(offset,2)!=@"\u" || !ushort.TryParse(value.AsSpan(offset+2,4),NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out var unit)) return "";
                    decoded.Append((char)unit);
                }
                value=decoded.ToString();
            }
            else if(value.StartsWith(@"\U",StringComparison.Ordinal)) {
                if(value.Length!=10 || !int.TryParse(value.AsSpan(2),NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out var escapedScalar) || !Rune.IsValid(escapedScalar)) return "";
                value=char.ConvertFromUtf32(escapedScalar);
            }
            var hex=value.StartsWith("U+",StringComparison.OrdinalIgnoreCase)?value[2..]:value.StartsWith("0x",StringComparison.OrdinalIgnoreCase)?value[2..]:value;
            if(hex.Length>=4 && int.TryParse(hex,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out var scalar) && Rune.IsValid(scalar))
                value=char.ConvertFromUtf32(scalar);
            if(value.Length==0 || !Rune.TryGetRuneAt(value,0,out var rune) || rune.Utf16SequenceLength!=value.Length) return "";
        }
        if(value.Length==0 || value.Length>32 || value.Any(char.IsControl) || StringInfo.ParseCombiningCharacters(value).Length!=1) return "";
        return value;
    }

    internal static TextBlock Create(PromptAction action,out string warning)
    {
        var fallback=action.Name switch { "翻译"=>"\uE774","解释"=>"\uE946","润色"=>"\uE70F",_=>"\uE73E" };
        var text=fallback;
        var family="Segoe Fluent Icons, Segoe MDL2 Assets";
        warning="";
        if(action.IconKind is PromptIconKind.Emoji or PromptIconKind.NerdFont) {
            var glyph=Glyph(action);
            if(glyph.Length==0) warning="请输入一个图标；暂时显示默认图标。";
            else if(action.IconKind==PromptIconKind.Emoji) { text=glyph; family="Segoe UI Emoji, Segoe UI Symbol"; }
            else {
                var font=string.IsNullOrWhiteSpace(action.IconFont)?NerdFonts.FirstOrDefault():NerdFonts.FirstOrDefault(name=>string.Equals(name,action.IconFont,StringComparison.OrdinalIgnoreCase));
                if(font==null) warning="未找到所选 JetBrainsMono Nerd Font。安装字体并重启软件后生效；暂时显示默认图标。";
                else {
                    var typeface=new Typeface(new FontFamily(font),FontStyles.Normal,FontWeights.Normal,FontStretches.Normal);
                    if(!typeface.TryGetGlyphTypeface(out var face) || !face.CharacterToGlyphMap.TryGetValue(glyph.EnumerateRunes().First().Value,out var index) || index==0)
                        warning="此字体不包含该图标；暂时显示默认图标。";
                    else { text=glyph; family=font; }
                }
            }
        }
        return new TextBlock { Text=text,FontFamily=new FontFamily(family),FontSize=action.IconKind==PromptIconKind.Emoji?19:16,TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center };
    }
}
