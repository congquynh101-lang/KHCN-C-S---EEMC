using System.Windows;
using EEMC.WM.App.ViewModels;

namespace EEMC.WM.App.Views;

public partial class TaskEditorWindow : Window
{
    public TaskEditorWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is TaskEditorViewModel old) old.RequestClose -= OnRequestClose;
            if (e.NewValue is TaskEditorViewModel vm) vm.RequestClose += OnRequestClose;
        };
        // Ctrl+Enter lưu ngay cả khi đang ở ô nhập nhiều dòng (TextBox sẽ nuốt phím Enter)
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter &&
                (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0 &&
                DataContext is TaskEditorViewModel vm)
            {
                e.Handled = true;
                // Đẩy giá trị ô đang nhập vào ViewModel trước khi lưu
                if (System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.TextBox tb)
                    tb.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
                vm.SaveCommand.Execute(null);
            }
        };
        Loaded += (_, _) =>
        {
            TitleBox.Focus();
            TitleBox.CaretIndex = TitleBox.Text.Length;
        };
    }

    private void OnRequestClose(bool result)
    {
        DialogResult = result;
        Close();
    }
}
