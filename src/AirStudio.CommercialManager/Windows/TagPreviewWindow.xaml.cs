using System.Windows;
using AirStudio.CommercialManager.Core.Models;

namespace AirStudio.CommercialManager.Windows
{
    /// <summary>
    /// Window for previewing TAG file content
    /// </summary>
    public partial class TagPreviewWindow : Window
    {
        private readonly TagFile _tagFile;

        public TagPreviewWindow(TagFile tagFile)
        {
            InitializeComponent();
            _tagFile = tagFile;

            LoadContent();
        }

        private void LoadContent()
        {
            if (_tagFile == null)
            {
                FileNameLabel.Text = "--";
                CapsuleLabel.Text = "--";
                ScheduleLabel.Text = "--";
                ContentTextBox.Text = "(No TAG file)";
                return;
            }

            // File info
            FileNameLabel.Text = _tagFile.GenerateFileName();
            CapsuleLabel.Text = $"{_tagFile.CapsuleName} ({_tagFile.CutCount} cuts, {_tagFile.TotalDurationFormatted})";

            var scheduleInfo = $"{_tagFile.TxTime:hh\\:mm\\:ss} on {_tagFile.TxDate:dd/MM/yyyy}";
            if (_tagFile.RepeatDays > 0)
            {
                scheduleInfo += $" to {_tagFile.ToDate:dd/MM/yyyy} ({_tagFile.RepeatDays + 1} days)";
            }
            ScheduleLabel.Text = scheduleInfo;

            // Content with column headers
            var content = "Category\tTxTime\tSpotName\tCapsuleName\tDuration\tFlag\tDurationSec\tAudioPath\tAgencyCode\tSequenceToken\n";
            content += new string('-', 120) + "\n";
            content += _tagFile.GenerateContent();

            ContentTextBox.Text = content;
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_tagFile != null)
            {
                Clipboard.SetText(_tagFile.GenerateContent());
                MessageBox.Show("TAG content copied to clipboard.", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
