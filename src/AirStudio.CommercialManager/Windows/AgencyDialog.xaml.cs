using System.Windows;
using AirStudio.CommercialManager.Core.Models;

namespace AirStudio.CommercialManager.Windows
{
    /// <summary>
    /// Dialog for adding/editing agency details
    /// </summary>
    public partial class AgencyDialog : Window
    {
        public Agency Agency { get; private set; }

        /// <summary>
        /// Create dialog for adding a new agency
        /// </summary>
        public AgencyDialog()
        {
            InitializeComponent();
            Agency = new Agency();
            HeaderText.Text = "ADD AGENCY";
            AgencyNameTextBox.Focus();
        }

        /// <summary>
        /// Create dialog for editing an existing agency
        /// </summary>
        public AgencyDialog(Agency agency)
        {
            InitializeComponent();
            Agency = agency;
            HeaderText.Text = "EDIT AGENCY";

            // Populate fields
            AgencyNameTextBox.Text = agency.AgencyName;
            AddressTextBox.Text = agency.Address;
            PINTextBox.Text = agency.PIN;
            PhoneTextBox.Text = agency.Phone;
            EmailTextBox.Text = agency.Email;

            AgencyNameTextBox.Focus();
            AgencyNameTextBox.SelectAll();
        }

        /// <summary>
        /// Create dialog with pre-filled agency name (for inline add)
        /// </summary>
        public AgencyDialog(string agencyName)
        {
            InitializeComponent();
            Agency = new Agency { AgencyName = agencyName };
            HeaderText.Text = "ADD AGENCY";

            AgencyNameTextBox.Text = agencyName;
            AddressTextBox.Focus();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Validate
            if (string.IsNullOrWhiteSpace(AgencyNameTextBox.Text))
            {
                MessageBox.Show("Agency Name is required.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                AgencyNameTextBox.Focus();
                return;
            }

            // Update agency object
            Agency.AgencyName = AgencyNameTextBox.Text.Trim();
            Agency.Address = string.IsNullOrWhiteSpace(AddressTextBox.Text) ? null : AddressTextBox.Text.Trim();
            Agency.PIN = string.IsNullOrWhiteSpace(PINTextBox.Text) ? null : PINTextBox.Text.Trim();
            Agency.Phone = string.IsNullOrWhiteSpace(PhoneTextBox.Text) ? null : PhoneTextBox.Text.Trim();
            Agency.Email = string.IsNullOrWhiteSpace(EmailTextBox.Text) ? null : EmailTextBox.Text.Trim();

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
