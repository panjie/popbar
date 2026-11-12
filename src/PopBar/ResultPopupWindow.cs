using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media.Effects;
using System.Windows.Shell;

namespace PopBar;

internal abstract class ResultPopupWindow:Window
{
    const double CornerRadius=12;
    const double ShadowMargin=10;
    const double ResizeEdge=5;
    const double HeaderHeight=42;
    readonly DockPanel layout=new();
    readonly TextBlock heading=new() { Name="ResultHeading",FontSize=14,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center };
    readonly TextBlock model=new() { Name="ResultModel",FontSize=11,Margin=new Thickness(8,0,0,0),TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center };
    readonly Button close;

    protected ResultPopupWindow(FrameworkElement icon)
    {
        WindowStyle=WindowStyle.None;
        AllowsTransparency=true;
        ShowInTaskbar=false;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        UseLayoutRounding=true;
        Ui.Style(this);
        Background=Brushes.Transparent;

        var chrome=new WindowChrome { GlassFrameThickness=new Thickness(0),UseAeroCaptionButtons=false };
        WindowChrome.SetWindowChrome(this,chrome);
        var shadow=new Border {
            CornerRadius=new CornerRadius(CornerRadius),Background=Brushes.Black,IsHitTestVisible=false,
            Effect=new DropShadowEffect { BlurRadius=16,ShadowDepth=2,Direction=270,Opacity=0.30 }
        };
        var clip=new RectangleGeometry();
        var surface=new Border { Name="ResultSurface",BorderThickness=new Thickness(1),Child=layout,Clip=clip };
        surface.SetResourceReference(Border.BackgroundProperty,"SurfaceBrush");
        surface.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");
        surface.SizeChanged+=(_,e)=>clip.Rect=new Rect(e.NewSize);
        var frame=new Grid { Children={shadow,surface} };
        Content=new Grid { Children={frame} };

        void UpdateFrame()
        {
            var maximized=WindowState==WindowState.Maximized;
            var radius=maximized?0:CornerRadius;
            frame.Margin=new Thickness(maximized?0:ShadowMargin);
            shadow.Visibility=maximized?Visibility.Collapsed:Visibility.Visible;
            surface.CornerRadius=new CornerRadius(radius);
            clip.RadiusX=clip.RadiusY=radius;
            chrome.ResizeBorderThickness=new Thickness(maximized?0:ShadowMargin+ResizeEdge);
            chrome.CaptionHeight=1+HeaderHeight-(maximized?0:ResizeEdge);
        }
        StateChanged+=(_,_)=>UpdateFrame();
        UpdateFrame();

        var caption=new DockPanel { Margin=new Thickness(14,0,7,0) };
        var header=new Border { Name="ResultTitleBar",Height=HeaderHeight,BorderThickness=new Thickness(0,0,0,1),Child=caption };
        header.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");
        DockPanel.SetDock(header,Dock.Top);
        layout.Children.Add(header);

        close=Ui.GlyphButton("\uE8BB",T("CloseWindow"),Close);
        close.Name="CloseResultButton";
        close.Width=32;
        close.Height=30;
        close.VerticalAlignment=VerticalAlignment.Center;
        WindowChrome.SetIsHitTestVisibleInChrome(close,true);
        DockPanel.SetDock(close,Dock.Right);
        caption.Children.Add(close);
        var titleIcon=new Viewbox { Width=18,Height=18,Margin=new Thickness(0,0,8,0),VerticalAlignment=VerticalAlignment.Center,Child=icon };
        DockPanel.SetDock(titleIcon,Dock.Left);
        caption.Children.Add(titleIcon);
        model.SetResourceReference(TextBlock.ForegroundProperty,"MutedTextBrush");
        var labels=new Grid { Margin=new Thickness(0,0,6,0) };
        labels.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto,MaxWidth=240 });
        labels.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(model,1);
        labels.Children.Add(heading);
        labels.Children.Add(model);
        caption.Children.Add(labels);

        KeyDown+=(_,e)=> {
            if(e.Key!=Key.Escape) return;
            e.Handled=true;
            Close();
        };
    }

    protected void SetResultTitle(string name,string? modelName=null)
    {
        Title=$"PopBar · {name}";
        heading.Text=name;
        heading.ToolTip=name;
        model.Text=modelName??"";
        model.ToolTip=modelName;
        model.Visibility=string.IsNullOrWhiteSpace(modelName)?Visibility.Collapsed:Visibility.Visible;
        close.ToolTip=T("CloseWindow");
        AutomationProperties.SetName(close,T("CloseWindow"));
    }

    protected void SetResultContent(UIElement content) => layout.Children.Add(content);

    protected static Border CreateResultFooter(TextBlock status,out StackPanel buttons)
    {
        status.FontSize=12;
        status.VerticalAlignment=VerticalAlignment.Center;
        status.Margin=new Thickness(0,0,8,0);
        status.SetResourceReference(TextBlock.ForegroundProperty,"MutedTextBrush");
        status.SetBinding(FrameworkElement.ToolTipProperty,new System.Windows.Data.Binding(nameof(TextBlock.Text)) { Source=status });
        buttons=new StackPanel { Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center };
        DockPanel.SetDock(buttons,Dock.Right);
        var footer=new Border {
            Name="ResultFooter",BorderThickness=new Thickness(0,1,0,0),Padding=new Thickness(16,6,10,6),
            Child=new DockPanel { Children={buttons,status} }
        };
        footer.SetResourceReference(Border.BackgroundProperty,"WindowBackgroundBrush");
        footer.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");
        return footer;
    }

    protected static Button AddResultAction(Panel buttons,string name,string label,Action action,string style="SubtleButtonStyle")
    {
        var button=Ui.Button(label,action,style);
        button.Name=name;
        button.MinHeight=32;
        button.Padding=new Thickness(9,3,9,3);
        button.Margin=new Thickness(2,0,0,0);
        buttons.Children.Add(button);
        return button;
    }
}
