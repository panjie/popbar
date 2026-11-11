using System.Collections.ObjectModel;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;

namespace PopBar;

internal sealed class PromptOrderList:ListBox
{
    const string DragFormat="PopBar.PromptOrder";
    readonly ObservableCollection<PromptAction> prompts;
    readonly DispatcherTimer edgeScroll=new() { Interval=TimeSpan.FromMilliseconds(85) };
    PromptAction? candidate,dragged;
    Point pressPoint,dragPoint;
    ScrollViewer? scroll;
    ScrollContentPresenter? viewport;
    InsertionMarker? marker;
    AdornerLayer? markerLayer;

    internal PromptOrderList(ObservableCollection<PromptAction> prompts)
    {
        this.prompts=prompts;
        ItemsSource=prompts;
        AllowDrop=true;
        ScrollViewer.SetHorizontalScrollBarVisibility(this,ScrollBarVisibility.Disabled);
        TextSearch.SetTextPath(this,nameof(PromptAction.Name));
        edgeScroll.Tick+=(_,_)=>ScrollDuringDrag();
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var row=e.OriginalSource is DependencyObject source?ContainerFromElement(this,source) as ListBoxItem:null;
        candidate=row==null?null:ItemContainerGenerator.ItemFromContainer(row) as PromptAction;
        pressPoint=e.GetPosition(this);
        base.OnPreviewMouseLeftButtonDown(e);
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        candidate=null;
        base.OnPreviewMouseLeftButtonUp(e);
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if(e.LeftButton!=MouseButtonState.Pressed) candidate=null;
        if(candidate==null || dragged!=null || prompts.Count<2) return;

        var offset=e.GetPosition(this)-pressPoint;
        if(Math.Abs(offset.X)<SystemParameters.MinimumHorizontalDragDistance &&
           Math.Abs(offset.Y)<SystemParameters.MinimumVerticalDragDistance) return;

        dragged=candidate;
        candidate=null;
        scroll=FindVisualChild<ScrollViewer>(this);
        viewport=scroll==null?null:FindVisualChild<ScrollContentPresenter>(scroll);
        e.Handled=true;
        try { DragDrop.DoDragDrop(this,new DataObject(DragFormat,"move"),DragDropEffects.Move); }
        finally {
            ClearIndicator();
            dragged=null;
            scroll=null;
            viewport=null;
        }
    }

    protected override void OnPreviewDragOver(DragEventArgs e)
    {
        dragPoint=e.GetPosition(this);
        var insertion=IsOurDrag(e)?FindInsertion(dragPoint):null;
        e.Effects=insertion.HasValue?DragDropEffects.Move:DragDropEffects.None;
        e.Handled=true;
        if(insertion is { } slot) {
            ShowIndicator(slot.Y);
            if(ScrollDirection()!=0) edgeScroll.Start();
            else edgeScroll.Stop();
        } else ClearIndicator();
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        ClearIndicator();
        base.OnDragLeave(e);
    }

    protected override void OnPreviewDrop(DragEventArgs e)
    {
        var insertion=IsOurDrag(e)?FindInsertion(e.GetPosition(this)):null;
        ClearIndicator();
        e.Effects=DragDropEffects.None;
        e.Handled=true;
        if(insertion is not { } slot || dragged is not { } prompt) return;

        var from=prompts.IndexOf(prompt);
        if(from<0) return;
        var to=slot.Index>from?slot.Index-1:slot.Index;
        if(from!=to) prompts.Move(from,to);
        SelectedItem=prompt;
        ScrollIntoView(prompt);
        e.Effects=DragDropEffects.Move;
    }

    bool IsOurDrag(DragEventArgs e) => dragged!=null && e.Data.GetDataPresent(DragFormat,autoConvert:false);

    (int Index,double Y)? FindInsertion(Point point)
    {
        if(viewport==null) return null;
        var position=TranslatePoint(point,viewport);
        if(!new Rect(viewport.RenderSize).Contains(position)) return null;

        (int Index,double Y)? afterLast=null;
        for(var i=0;i<Items.Count;i++) {
            if(ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem row) continue;
            var top=row.TranslatePoint(new Point(),viewport).Y;
            var bottom=top+row.ActualHeight;
            if(bottom<=0 || top>=viewport.ActualHeight) continue;
            if(position.Y<(top+bottom)/2) return (i,Math.Max(0,top));
            afterLast=(i+1,Math.Min(viewport.ActualHeight,bottom));
        }
        return afterLast;
    }

    int ScrollDirection()
    {
        if(viewport==null || scroll==null) return 0;
        var y=TranslatePoint(dragPoint,viewport).Y;
        var edge=Math.Min(24,viewport.ActualHeight/3);
        if(y<edge && scroll.VerticalOffset>0) return -1;
        if(y>viewport.ActualHeight-edge && scroll.VerticalOffset<scroll.ScrollableHeight) return 1;
        return 0;
    }

    void ScrollDuringDrag()
    {
        var direction=ScrollDirection();
        if(direction==0 || scroll==null) { edgeScroll.Stop(); return; }
        if(direction<0) scroll.LineUp();
        else scroll.LineDown();
        scroll.UpdateLayout();
        if(FindInsertion(dragPoint) is { } slot) ShowIndicator(slot.Y);
    }

    void ShowIndicator(double y)
    {
        if(marker==null && viewport!=null) {
            markerLayer=AdornerLayer.GetAdornerLayer(viewport);
            if(markerLayer==null) return;
            marker=new InsertionMarker(viewport);
            markerLayer.Add(marker);
        }
        marker?.SetPosition(y);
    }

    void ClearIndicator()
    {
        edgeScroll.Stop();
        if(marker!=null) markerLayer?.Remove(marker);
        marker=null;
        markerLayer=null;
    }

    static T? FindVisualChild<T>(DependencyObject parent) where T:DependencyObject
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++) {
            var child=VisualTreeHelper.GetChild(parent,i);
            if(child is T match) return match;
            if(FindVisualChild<T>(child) is { } descendant) return descendant;
        }
        return null;
    }

    sealed class InsertionMarker(FrameworkElement viewport):Adorner(viewport)
    {
        double y;

        internal void SetPosition(double position)
        {
            IsHitTestVisible=false;
            y=position;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var brush=viewport.TryFindResource("AccentBrush") as Brush??SystemColors.HighlightBrush;
            var top=Math.Clamp(y-1,0,Math.Max(0,ActualHeight-2));
            drawingContext.DrawRoundedRectangle(brush,null,new Rect(3,top,Math.Max(0,ActualWidth-6),2),1,1);
        }
    }
}
