using System.Globalization;
using System.Text.Json;

namespace PopBar;

internal sealed record NerdIcon(string Name,string Glyph,int Code)
{
    public string CodePoint=>$"U+{Code:X4}";
}

internal static class NerdIconCatalog
{
    // Nerd Fonts v3.4.0 glyphnames.json; license shipped alongside the application.
    static readonly Lazy<NerdIcon[]> entries=new(()=> {
        using var stream=typeof(NerdIconCatalog).Assembly.GetManifestResourceStream("PopBar.Assets.nerd-font-glyphnames.json")!;
        using var document=JsonDocument.Parse(stream);
        return document.RootElement.EnumerateObject().Where(item=>item.Name!="METADATA")
            .Select(item=>new NerdIcon(item.Name,item.Value.GetProperty("char").GetString()!,int.Parse(item.Value.GetProperty("code").GetString()!,NumberStyles.HexNumber,CultureInfo.InvariantCulture)))
            .OrderBy(item=>item.Name,StringComparer.Ordinal).ToArray();
    });
    internal static NerdIcon[] Entries=>entries.Value;
    internal static NerdIcon[] Search(IEnumerable<NerdIcon> source,string query)
    {
        var terms=query.Trim().Split(' ',StringSplitOptions.RemoveEmptyEntries);
        var glyph=PromptIcons.Glyph(new PromptAction { IconKind=PromptIconKind.NerdFont,Icon=query });
        return source.Where(item=>(glyph.Length>0 && item.Glyph==glyph) || terms.All(term=>
            item.Name.Contains(term,StringComparison.OrdinalIgnoreCase) || item.CodePoint.Contains(term,StringComparison.OrdinalIgnoreCase))).ToArray();
    }
}

internal sealed class NerdIconPicker : Window
{
    internal string? SelectedGlyph { get; private set; }
    internal NerdIconPicker(string? requestedFont) : this(ResolveFont(requestedFont)) { }

    static FontFamily? ResolveFont(string? requestedFont)
    {
        var name=string.IsNullOrWhiteSpace(requestedFont)?PromptIcons.NerdFonts.FirstOrDefault():PromptIcons.NerdFonts.FirstOrDefault(name=>string.Equals(name,requestedFont,StringComparison.OrdinalIgnoreCase));
        return name==null?null:new FontFamily(name);
    }

    internal NerdIconPicker(FontFamily? font)
    {
        Title="选择 Nerd Font 图标"; Width=680; Height=580; MinWidth=420; MinHeight=360;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Ui.Style(this);
        var root=new DockPanel { Margin=new Thickness(20) }; Content=root;
        var top=new StackPanel(); DockPanel.SetDock(top,Dock.Top); root.Children.Add(top);
        top.Children.Add(Ui.Label(font?.FamilyNames.Values.FirstOrDefault()??"未找到 JetBrainsMono Nerd Font，请安装字体并重启 PopBar。",true));
        top.Children.Add(Ui.Label("搜索英文名称（如 translate、code）、码点，或粘贴图标"));
        var search=Ui.Input(""); search.Name="NerdIconSearch"; top.Children.Add(search);
        var status=Ui.Label("",true); status.Name="NerdIconStatus"; top.Children.Add(status);
        var bottom=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0) };
        DockPanel.SetDock(bottom,Dock.Bottom); root.Children.Add(bottom);
        var page=0;
        var previous=Ui.Button("上一页",()=>{}); var next=Ui.Button("下一页",()=>{});
        bottom.Children.Add(previous); bottom.Children.Add(next);
        var cancel=Ui.Button("取消",Close); cancel.IsCancel=true; bottom.Children.Add(cancel);
        var tiles=new WrapPanel();
        var scroll=new ScrollViewer { Content=tiles,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled }; root.Children.Add(scroll);
        GlyphTypeface? face=null;
        if(font!=null) new Typeface(font,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal).TryGetGlyphTypeface(out face);
        var available=face==null?Array.Empty<NerdIcon>():NerdIconCatalog.Entries.Where(item=>face.CharacterToGlyphMap.TryGetValue(item.Code,out var index)&&index!=0).ToArray();
        var matches=available;
        const int pageSize=80;
        void Render()
        {
            tiles.Children.Clear(); scroll.ScrollToTop();
            var pages=Math.Max(1,(matches.Length+pageSize-1)/pageSize);
            status.Text=matches.Length==0?"没有匹配的图标。请检查搜索内容和所选字体。":$"{matches.Length} 个图标 · 第 {page+1} / {pages} 页 · 点击图标选用";
            previous.IsEnabled=page>0; next.IsEnabled=page+1<pages;
            foreach(var item in matches.Skip(page*pageSize).Take(pageSize)) {
                var button=Ui.Button("",()=> { SelectedGlyph=item.Glyph; DialogResult=true; });
                button.Width=68; button.Height=64; button.Margin=new Thickness(3); button.Padding=new Thickness(2);
                button.ToolTip=$"{item.Name}\n{item.CodePoint}";
                System.Windows.Automation.AutomationProperties.SetName(button,$"{item.Name} {item.CodePoint}");
                var content=new StackPanel();
                content.Children.Add(new TextBlock { Text=item.Glyph,FontFamily=font!,FontSize=25,TextAlignment=TextAlignment.Center });
                var code=new TextBlock { Text=item.CodePoint,FontSize=9,TextAlignment=TextAlignment.Center }; code.SetResourceReference(TextBlock.ForegroundProperty,"MutedTextBrush"); content.Children.Add(code);
                button.Content=content; tiles.Children.Add(button);
            }
        }
        search.TextChanged+=(_,_)=> { page=0; matches=NerdIconCatalog.Search(available,search.Text); Render(); };
        previous.Click+=(_,_)=> { page--; Render(); }; next.Click+=(_,_)=> { page++; Render(); };
        Render(); Loaded+=(_,_)=>search.Focus();
    }
}
