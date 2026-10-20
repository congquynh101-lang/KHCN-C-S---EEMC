using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EEMC.WM.App.ViewModels;

namespace EEMC.WM.App.Views;

public partial class CalendarView : UserControl
{
    private Point _dragStart;
    private CalendarEntry? _dragEntry;
    private const string DragFormat = "EEMC.CalendarEntry";

    public CalendarView() => InitializeComponent();

    private void OnChipMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _dragEntry = (sender as FrameworkElement)?.DataContext as CalendarEntry;
    }

    private void OnChipMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragEntry == null) return;
        var p = e.GetPosition(this);
        if (Math.Abs(p.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(p.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var entry = _dragEntry;
        _dragEntry = null;
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(DragFormat, entry), DragDropEffects.Move);
    }

    private void OnDayDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DragFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDayDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not CalendarViewModel vm) return;
        if (e.Data.GetData(DragFormat) is not CalendarEntry entry) return;
        if ((sender as FrameworkElement)?.DataContext is not CalendarDay day) return;
        e.Handled = true;
        await vm.MoveAsync(entry, day.Date);
    }
}
