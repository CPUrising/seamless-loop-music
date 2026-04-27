using System.Windows.Controls;
using System.Windows.Input;

namespace seamless_loop_music.UI.Views
{
    public partial class LibraryView : UserControl
    {
        public LibraryView()
        {
            InitializeComponent();
        }

        private void ListBoxItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem item)
            {
                item.IsSelected = true;
                item.Focus();
            }
        }

        private void CategoryListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CategoryListBox.SelectedItem != null)
            {
                // 延迟一点执行滚动，确保界面已渲染
                Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    CategoryListBox.ScrollIntoView(CategoryListBox.SelectedItem);
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }
    }
}

