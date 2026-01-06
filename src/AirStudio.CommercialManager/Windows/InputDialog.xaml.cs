using System.Windows;

namespace AirStudio.CommercialManager.Windows
{
    /// <summary>
    /// Simple input dialog for WPF
    /// </summary>
    public partial class InputDialog : Window
    {
        public string InputValue { get; private set; }

        public InputDialog(string prompt, string title = "Input", string defaultValue = "")
        {
            InitializeComponent();
            Title = title;
            PromptText.Text = prompt;
            InputTextBox.Text = defaultValue;
            InputTextBox.SelectAll();
            InputTextBox.Focus();
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            InputValue = InputTextBox.Text;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        /// <summary>
        /// Show the input dialog and return the result
        /// </summary>
        public static string Show(string prompt, string title = "Input", string defaultValue = "", Window owner = null)
        {
            var dialog = new InputDialog(prompt, title, defaultValue);
            if (owner != null)
                dialog.Owner = owner;

            if (dialog.ShowDialog() == true)
                return dialog.InputValue;

            return null;
        }
    }
}
