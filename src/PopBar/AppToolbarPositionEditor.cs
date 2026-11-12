using System.Windows.Automation;

namespace PopBar;

internal sealed class AppToolbarPositionEditor:StackPanel
{
    sealed record Row(string[] Executables,ComboBox Position);

    readonly List<Row> rows=new();

    internal AppToolbarPositionEditor(IEnumerable<AppToolbarPosition> positions)
    {
        var rules=new StackPanel();
        Children.Add(rules);
        var customOption=new AppToolbarPresets.Preset(T("ToolbarCustomApp"),[]);
        var picker=new ComboBox {
            Name="AppToolbarPresetSelector",DisplayMemberPath=nameof(AppToolbarPresets.Preset.Label),MaxDropDownHeight=300
        };
        picker.SetResourceReference(StyleProperty,"ComboBoxStyle");
        AutomationProperties.SetName(picker,T("ToolbarAddApp"));
        var customLabel=Ui.Label(T("ToolbarExecutableHint"),muted:true);
        var custom=Ui.Input("");
        custom.Name="AppToolbarExecutableInput";
        custom.MinHeight=36;
        AutomationProperties.SetLabeledBy(custom,customLabel);

        bool IsAvailable(string exe) => !rows.Any(row=>row.Executables.Contains(exe,StringComparer.OrdinalIgnoreCase));

        string[] NewExecutables()
        {
            if(picker.SelectedItem is not AppToolbarPresets.Preset preset) return [];
            var candidates=preset.Executables;
            if(preset==customOption) {
                var name=custom.Text.Trim();
                if(name.Length==0 || name.EndsWith('.') || name.IndexOfAny(Path.GetInvalidFileNameChars())>=0) return [];
                candidates=[name.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)?name:name+".exe"];
            }
            return candidates.Where(IsAvailable).ToArray();
        }

        var add=new Button {
            Name="AppToolbarAddButton",Content=T("ToolbarAddApp"),Margin=new Thickness(8,0,0,0),VerticalAlignment=VerticalAlignment.Bottom,
            Cursor=System.Windows.Input.Cursors.Hand
        };
        add.SetResourceReference(StyleProperty,"SecondaryButtonStyle");
        add.Click+=(_,_)=> {
            var executables=NewExecutables();
            if(executables.Length==0) return;
            AddRule(executables,ToolbarPlacement.Below);
            if(ReferenceEquals(picker.SelectedItem,customOption)) custom.Clear();
            RefreshPresets();
        };
        DockPanel.SetDock(add,Dock.Right);
        var inputs=new StackPanel { Children={picker,customLabel,custom} };
        Children.Add(new DockPanel { Margin=new Thickness(0,10,0,0),Children={add,inputs} });

        void UpdateAdd()
        {
            customLabel.Visibility=custom.Visibility=ReferenceEquals(picker.SelectedItem,customOption)?Visibility.Visible:Visibility.Collapsed;
            add.IsEnabled=NewExecutables().Length>0;
        }

        void RefreshPresets()
        {
            var selected=picker.SelectedItem as AppToolbarPresets.Preset;
            var available=AppToolbarPresets.All.Where(preset=>preset.Executables.Any(IsAvailable)).Prepend(customOption).ToArray();
            picker.ItemsSource=available;
            picker.SelectedItem=available.Contains(selected)?selected:customOption;
            picker.Visibility=available.Length>1?Visibility.Visible:Visibility.Collapsed;
            UpdateAdd();
        }

        void AddRule(string[] executables,ToolbarPlacement placement)
        {
            var names=string.Join(", ",executables);
            var title=AppToolbarPresets.All.FirstOrDefault(preset=>preset.Executables.SequenceEqual(executables,StringComparer.OrdinalIgnoreCase))?.Label??names;
            var position=PositionSelector(placement);
            position.Name="AppToolbarPositionSelector";
            position.Width=104;
            position.Margin=new Thickness(8,0,0,0);
            AutomationProperties.SetName(position,T("ToolbarAppPositionFor",title));
            var rule=new Row(executables,position);
            rows.Add(rule);
            var row=new DockPanel { Margin=new Thickness(0,0,0,6) };
            var remove=Ui.Button(T("ToolbarRemoveApp"),()=> { rows.Remove(rule); rules.Children.Remove(row); RefreshPresets(); });
            remove.Name="AppToolbarRemoveButton";
            remove.Margin=new Thickness(8,0,0,0);
            AutomationProperties.SetName(remove,T("ToolbarRemoveAppNamed",title));
            DockPanel.SetDock(remove,Dock.Right);
            DockPanel.SetDock(position,Dock.Right);
            row.Children.Add(remove);
            row.Children.Add(position);
            row.Children.Add(new TextBlock { Text=title,ToolTip=names,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis });
            rules.Children.Add(row);
        }

        foreach(var rule in positions)
            if(rule?.Executables is { Length:>0 }) AddRule(rule.Executables,rule.Position);
        picker.SelectionChanged+=(_,_)=>UpdateAdd();
        custom.TextChanged+=(_,_)=>UpdateAdd();
        RefreshPresets();
    }

    internal static ComboBox PositionSelector(ToolbarPlacement placement)
    {
        var selector=new ComboBox();
        selector.SetResourceReference(StyleProperty,"ComboBoxStyle");
        selector.Items.Add(new ComboBoxItem { Content=T("Above"),Tag=ToolbarPlacement.Above });
        selector.Items.Add(new ComboBoxItem { Content=T("Below"),Tag=ToolbarPlacement.Below });
        selector.SelectedIndex=placement==ToolbarPlacement.Below?1:0;
        return selector;
    }

    internal List<AppToolbarPosition> ReadPositions() => rows.Select(row=>new AppToolbarPosition(
        (string[])row.Executables.Clone(),row.Position.SelectedIndex==1?ToolbarPlacement.Below:ToolbarPlacement.Above)).ToList();
}
