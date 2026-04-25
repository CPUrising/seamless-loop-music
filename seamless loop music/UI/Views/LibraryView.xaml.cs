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
    }
}

