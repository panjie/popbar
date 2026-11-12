namespace PopBar;

internal static class AppToolbarPresets
{
    internal sealed record Preset(string Label,string[] Executables);

    internal static IReadOnlyList<Preset> All { get; }=[
        new("Microsoft Teams",["ms-teams.exe","Teams.exe"]),
        new("Microsoft Outlook",["olk.exe","OUTLOOK.EXE"]),
        new("Microsoft Word",["WINWORD.EXE"]),
        new("Microsoft Excel",["EXCEL.EXE"]),
        new("Microsoft PowerPoint",["POWERPNT.EXE"]),
        new("Notion",["Notion.exe"]),
        new("Adobe Acrobat / Reader",["Acrobat.exe","AcroRd32.exe"]),
        new("Foxit PDF Reader / Editor",["FoxitPDFReader.exe","FoxitReader.exe","FoxitPDFEditor.exe","FoxitPhantomPDF.exe"]),
        new("Microsoft Edge",["msedge.exe"]),
        new("Opera",["opera.exe"])
    ];

    internal static List<AppToolbarPosition> CreateDefaults()
    {
        var positions=new List<AppToolbarPosition>();
        AddMissing(positions);
        return positions;
    }

    internal static void AddMissing(List<AppToolbarPosition> positions)
    {
        var configured=positions.SelectMany(rule=>rule?.Executables??[]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var preset in All) {
            var missing=preset.Executables.Where(configured.Add).ToArray();
            if(missing.Length>0) positions.Add(new(missing,ToolbarPlacement.Below));
        }
    }
}
