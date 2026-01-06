using System;
using System.Windows;
using Microsoft.Win32;
using AirStudio.CommercialManager.Core.Models;
using AirStudio.CommercialManager.Core.Services.Audio;

namespace AirStudio.CommercialManager.Windows
{
    /// <summary>
    /// Dialog for adding/editing commercial details
    /// </summary>
    public partial class CommercialDialog : Window
    {
        private readonly Channel _channel;
        private readonly AudioService _audioService = new AudioService();
        private bool _isEditMode;

        public Commercial Commercial { get; private set; }
        public string AudioFilePath { get; private set; }

        /// <summary>
        /// Create dialog for adding a new commercial
        /// </summary>
        public CommercialDialog(Channel channel)
        {
            InitializeComponent();
            _channel = channel;
            Commercial = new Commercial();
            _isEditMode = false;

            HeaderText.Text = "ADD COMMERCIAL";
            AgencyCombo.Initialize(channel);
            SpotTextBox.Focus();
        }

        /// <summary>
        /// Create dialog for editing an existing commercial
        /// </summary>
        public CommercialDialog(Channel channel, Commercial commercial)
        {
            InitializeComponent();
            _channel = channel;
            Commercial = commercial;
            _isEditMode = true;

            HeaderText.Text = "EDIT COMMERCIAL";
            AgencyCombo.Initialize(channel);

            // Populate fields
            if (commercial.Code > 0)
            {
                AgencyCombo.SetSelectedAgencyCode(commercial.Code);
            }
            SpotTextBox.Text = commercial.Spot;
            TitleTextBox.Text = commercial.Title;
            DurationTextBox.Text = commercial.Duration;
            OtherinfoTextBox.Text = commercial.Otherinfo;

            if (!string.IsNullOrEmpty(commercial.Filename))
            {
                AudioFileTextBox.Text = commercial.Filename;
                DurationLabel.Text = $"Duration: {commercial.Duration}";
            }

            SpotTextBox.Focus();
            SpotTextBox.SelectAll();
        }

        /// <summary>
        /// Create dialog with pre-filled data from drag-drop
        /// </summary>
        public CommercialDialog(Channel channel, Commercial commercial, string audioPath) : this(channel, commercial)
        {
            if (!string.IsNullOrEmpty(audioPath))
            {
                AudioFilePath = audioPath;
                AudioFileTextBox.Text = audioPath;
                UpdateAudioDuration(audioPath);
            }
        }

        private void BrowseAudioButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Audio files (*.wav;*.mp3;*.aiff;*.m4a;*.wma;*.ogg;*.flac)|*.wav;*.mp3;*.aiff;*.m4a;*.wma;*.ogg;*.flac|All files (*.*)|*.*",
                Title = "Select Audio File"
            };

            if (dialog.ShowDialog() == true)
            {
                AudioFilePath = dialog.FileName;
                AudioFileTextBox.Text = dialog.FileName;
                UpdateAudioDuration(dialog.FileName);

                // Auto-fill spot name if empty
                if (string.IsNullOrWhiteSpace(SpotTextBox.Text))
                {
                    var spotName = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
                    SpotTextBox.Text = Commercial.SanitizeForFilename(spotName);
                }
            }
        }

        private void UpdateAudioDuration(string audioPath)
        {
            try
            {
                var duration = _audioService.GetAudioDuration(audioPath);
                DurationTextBox.Text = AudioService.FormatDuration(duration);
                DurationLabel.Text = $"Duration: {AudioService.FormatDuration(duration)} ({duration.TotalSeconds:F2} seconds)";
            }
            catch (Exception ex)
            {
                DurationLabel.Text = $"Duration: Unable to read ({ex.Message})";
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Validate
            var agency = AgencyCombo.SelectedAgency;
            if (agency == null)
            {
                MessageBox.Show("Please select an agency.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(SpotTextBox.Text))
            {
                MessageBox.Show("Spot name is required.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                SpotTextBox.Focus();
                return;
            }

            // For new commercials, audio file is required
            if (!_isEditMode && string.IsNullOrEmpty(AudioFilePath))
            {
                MessageBox.Show("Please select an audio file for new commercials.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Update commercial object
            Commercial.Code = agency.Code;
            Commercial.Agency = agency.AgencyName;
            Commercial.Spot = Commercial.SanitizeForFilename(SpotTextBox.Text.Trim());
            Commercial.Title = string.IsNullOrWhiteSpace(TitleTextBox.Text) ? null : TitleTextBox.Text.Trim();
            Commercial.Duration = string.IsNullOrWhiteSpace(DurationTextBox.Text) ? null : DurationTextBox.Text.Trim();
            Commercial.Otherinfo = string.IsNullOrWhiteSpace(OtherinfoTextBox.Text) ? null : OtherinfoTextBox.Text.Trim();

            // Set filename if audio provided
            if (!string.IsNullOrEmpty(AudioFilePath))
            {
                Commercial.Filename = $"{Commercial.Spot}.WAV";
            }

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
