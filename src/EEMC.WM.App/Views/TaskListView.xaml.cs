using System.Windows.Controls;
using System.Windows.Input;
using EEMC.WM.App.ViewModels;

namespace EEMC.WM.App.Views;

public partial class TaskListView : UserControl
{
    public TaskListView() => InitializeComponent();

    private TaskListViewModel? Vm => DataContext as TaskListViewModel;

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Vm == null) return;
        Vm.SelectedItems.Clear();
        Vm.SelectedItems.AddRange(TaskGrid.SelectedItems.OfType<TaskItemViewModel>());
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm == null || Vm.IsTrash) return;
        if (e.OriginalSource is System.Windows.DependencyObject d && ItemsControl.ContainerFromElement(TaskGrid, d) is DataGridRow row
            && row.Item is TaskItemViewModel item)
            item.EditCommand.Execute(null);
    }

    private void OnGridKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm == null) return;
        if (e.Key == Key.Enter && !Vm.IsTrash)
        {
            Vm.EditCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            if (Vm.IsTrash) Vm.PurgeCommand.Execute(null);
            else Vm.DeleteCommand.Execute(null);
            e.Handled = true;
        }
    }
}
