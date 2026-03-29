using System.Windows;
using System.Windows.Input;

namespace seamless_loop_music.UI.Views
{
    public partial class InputDialog : Window
    {
        public string InputText { get; set; }
        public string Message { get; set; }

        public InputDialog(string title, string message, string defaultText = "")
        {
            InitializeComponent();
            this.DataContext = this;
            this.Title = title;
            this.Message = message;
            this.InputText = defaultText;
            
            this.Loaded += (s, e) => {
                InputTextBox.Focus();
                InputTextBox.SelectAll();
            };
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(InputTextBox.Text))
            {
                MessageBox.Show("名称不能为空喵！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            this.DialogResult = true;
            this.InputText = InputTextBox.Text;
            this.Close();
        }

        private void InputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OkButton_Click(this, new RoutedEventArgs());
            }
            else if (e.Key == Key.Escape)
            {
                this.DialogResult = false;
                this.Close();
            }
        }
    }
}
