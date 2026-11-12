namespace PopBar;

internal static class BuiltInPrompts
{
    static readonly (string NameKey,string Template,string Glyph)[] defaults={
        ("TranslatePrompt","将以下文字翻译成简体中文，仅返回译文：\n{{text}}","🌐"),
        ("ExplainPrompt","用中文清晰解释以下文字或代码：\n{{text}}","💡"),
        ("PolishPrompt","润色以下文字，保留原意和原语言，仅返回润色结果：\n{{text}}","✍️")
    };

    internal static List<PromptAction> Create() => defaults
        .Select(item=>new PromptAction { Name=T(item.NameKey),Prompt=item.Template,Icon=item.Glyph }).ToList();

    internal static void LocalizeName(PromptAction prompt)
    {
        foreach(var item in defaults) {
            if(prompt.Prompt!=item.Template || !IsTranslation(item.NameKey,prompt.Name)) continue;
            prompt.Name=T(item.NameKey);
            return;
        }
    }

    internal static string LegacyEmoji(string name)
    {
        foreach(var item in defaults)
            if(IsTranslation(item.NameKey,name)) return item.Glyph;
        return "✨";
    }
}
