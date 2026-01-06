using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AirStudio.CommercialManager.Core.Models;
using AirStudio.CommercialManager.Core.Services.Logging;
using AirStudio.CommercialManager.Core.Services.Tags;
using AirStudio.CommercialManager.Windows;
using Microsoft.Win32;

namespace AirStudio.CommercialManager.Controls
{
    /// <summary>
    /// Control for importing and editing TAG files
    /// </summary>
    public partial class TagEditorControl : UserControl
    {
        private Channel _channel;
        private TagService _tagService;
        private TagFile _tagFile;
        private TagFile _originalTagFile;
        private TagEntry _selectedEntry;
        private bool _hasChanges;

        public TagEditorControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Initialize with a channel
        /// </summary>
        public void Initialize(Channel channel)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _tagService = new TagService(channel);

            LoadTagFileList();
        }

        private void LoadTagFileList()
        {
            TagFileComboBox.Items.Clear();

            var tagFiles = _tagService.ListTagFiles();

            foreach (var file in tagFiles)
            {
                TagFileComboBox.Items.Add(new ComboBoxItem
                {
                    Content = Path.GetFileName(file),
                    Tag = file
                });
            }

            if (tagFiles.Count > 0)
            {
                StatusLabel.Text = $"Found {tagFiles.Count} TAG file(s). Select one to edit.";
            }
            else
            {
                StatusLabel.Text = "No TAG files found in the Commercial Playlist folder.";
            }
        }

        private void LoadTagFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                ClearEditor();
                StatusLabel.Text = "TAG file not found.";
                return;
            }

            try
            {
                _tagFile = _tagService.LoadTagFile(filePath);
                _originalTagFile = _tagService.LoadTagFile(filePath); // Keep original for comparison

                if (_tagFile == null || _tagFile.Entries.Count == 0)
                {
                    ClearEditor();
                    StatusLabel.Text = "Failed to parse TAG file or file is empty.";
                    return;
                }

                // Update UI
                CapsuleLabel.Text = _tagFile.CapsuleName ?? "--";
                TxTimeLabel.Text = _tagFile.TxTime.ToString(@"hh\:mm\:ss");
                TxDateLabel.Text = _tagFile.TxDate.ToString("dd/MM/yyyy");
                ToDateLabel.Text = _tagFile.ToDate.ToString("dd/MM/yyyy");
                CutCountLabel.Text = _tagFile.CutCount.ToString();

                EntriesGrid.ItemsSource = _tagFile.Entries;

                _hasChanges = false;
                UpdateButtonStates();

                StatusLabel.Text = $"Loaded: {_tagFile.FileName}";
                LogService.Info($"Loaded TAG for editing: {_tagFile.FileName}");
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to load TAG file", ex);
                StatusLabel.Text = $"Error loading TAG: {ex.Message}";
            }
        }

        private void ClearEditor()
        {
            _tagFile = null;
            _originalTagFile = null;
            _selectedEntry = null;
            _hasChanges = false;

            CapsuleLabel.Text = "--";
            TxTimeLabel.Text = "--";
            TxDateLabel.Text = "--";
            ToDateLabel.Text = "--";
            CutCountLabel.Text = "--";

            EntriesGrid.ItemsSource = null;
            ClearEntryEditor();
            UpdateButtonStates();
        }

        private void ClearEntryEditor()
        {
            EditPanel.IsEnabled = false;
            SpotNameTextBox.Text = "";
            CapsuleNameTextBox.Text = "";
            AudioPathTextBox.Text = "";
            AgencyCodeTextBox.Text = "";
            EntryWaveform.Clear();
        }

        private void LoadEntryEditor(TagEntry entry)
        {
            if (entry == null)
            {
                ClearEntryEditor();
                return;
            }

            _selectedEntry = entry;
            EditPanel.IsEnabled = !entry.IsTerminator; // Can't edit terminator

            SpotNameTextBox.Text = entry.SpotName ?? "";
            CapsuleNameTextBox.Text = entry.CapsuleName ?? "";
            AudioPathTextBox.Text = entry.AudioPath ?? "";
            AgencyCodeTextBox.Text = entry.AgencyCode.ToString();

            // Load waveform if audio file exists
            if (!string.IsNullOrEmpty(entry.AudioPath) && File.Exists(entry.AudioPath))
            {
                EntryWaveform.LoadAudio(entry.AudioPath);
            }
            else
            {
                EntryWaveform.Clear();
            }
        }

        private void ApplyEntryChanges()
        {
            if (_selectedEntry == null) return;

            _selectedEntry.SpotName = SpotNameTextBox.Text?.Trim() ?? "";
            _selectedEntry.CapsuleName = CapsuleNameTextBox.Text?.Trim() ?? "";
            _selectedEntry.AudioPath = AudioPathTextBox.Text?.Trim() ?? "";

            if (int.TryParse(AgencyCodeTextBox.Text, out var agencyCode))
            {
                _selectedEntry.AgencyCode = agencyCode;
            }

            // Update capsule name in the header if changed
            if (!string.IsNullOrEmpty(_selectedEntry.CapsuleName))
            {
                _tagFile.CapsuleName = _selectedEntry.CapsuleName;
                CapsuleLabel.Text = _tagFile.CapsuleName;
            }

            // Refresh grid
            EntriesGrid.Items.Refresh();

            _hasChanges = true;
            UpdateButtonStates();

            StatusLabel.Text = "Changes applied. Click 'Save TAG' to save.";
        }

        private void UpdateButtonStates()
        {
            SaveButton.IsEnabled = _tagFile != null && _hasChanges;
            PreviewButton.IsEnabled = _tagFile != null && _hasChanges;
        }

        private bool WillFilenameChange()
        {
            if (_tagFile == null || _originalTagFile == null)
                return false;

            var newFilename = _tagFile.GenerateFileName();
            var oldFilename = _originalTagFile.GenerateFileName();

            return !newFilename.Equals(oldFilename, StringComparison.OrdinalIgnoreCase);
        }

        #region Event Handlers

        private void TagFileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TagFileComboBox.SelectedItem is ComboBoxItem item && item.Tag is string filePath)
            {
                if (_hasChanges)
                {
                    var result = MessageBox.Show(
                        "You have unsaved changes. Discard them?",
                        "Unsaved Changes",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result != MessageBoxResult.Yes)
                    {
                        e.Handled = true;
                        return;
                    }
                }

                LoadTagFile(filePath);
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadTagFileList();
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select TAG File",
                Filter = "TAG Files (*.TAG)|*.TAG|All Files (*.*)|*.*",
                InitialDirectory = _tagService.GetPrimaryTagFolder() ?? ""
            };

            if (dialog.ShowDialog() == true)
            {
                LoadTagFile(dialog.FileName);
            }
        }

        private void EntriesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (EntriesGrid.SelectedItem is TagEntry entry)
            {
                LoadEntryEditor(entry);
            }
        }

        private void BrowseAudioButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Audio File",
                Filter = "Audio Files (*.WAV;*.MP3)|*.WAV;*.MP3|All Files (*.*)|*.*",
                InitialDirectory = _channel?.PrimaryCommercialsPath ?? ""
            };

            if (dialog.ShowDialog() == true)
            {
                AudioPathTextBox.Text = dialog.FileName;
            }
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyEntryChanges();
        }

        private void RevertButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedEntry != null)
            {
                LoadEntryEditor(_selectedEntry);
            }
        }

        private void PreviewButton_Click(object sender, RoutedEventArgs e)
        {
            if (_tagFile == null) return;

            var previewWindow = new TagPreviewWindow(_tagFile);
            previewWindow.Owner = Window.GetWindow(this);
            previewWindow.ShowDialog();
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_tagFile == null) return;

            try
            {
                SaveButton.IsEnabled = false;
                SaveButton.Content = "Saving...";

                var willRename = WillFilenameChange();
                var oldPath = _tagFile.FilePath;

                TagSaveResult saveResult;

                if (willRename && !string.IsNullOrEmpty(oldPath))
                {
                    // Rename and save
                    saveResult = await _tagService.RenameTagFileAsync(oldPath, _tagFile);

                    if (saveResult.AnySucceeded)
                    {
                        // Update playlist with new path
                        await _tagService.UpdatePlaylistPathAsync(
                            oldPath,
                            _tagFile,
                            "", // userName - could get from dialog
                            ""); // mobileNo
                    }
                }
                else
                {
                    // Just overwrite in place
                    saveResult = await _tagService.SaveTagFileAsync(_tagFile);
                }

                // Replicate to missing targets if requested
                if (ReplicateCheckBox.IsChecked == true && saveResult.AnySucceeded)
                {
                    var replicated = await _tagService.ReplicateMissingTagAsync(saveResult.PrimaryPath);
                    if (replicated > 0)
                    {
                        StatusLabel.Text = $"Saved and replicated to {replicated} additional target(s).";
                    }
                }

                if (saveResult.AnySucceeded)
                {
                    _hasChanges = false;
                    _originalTagFile = _tagService.LoadTagFile(saveResult.PrimaryPath);
                    UpdateButtonStates();

                    var msg = willRename
                        ? $"TAG file renamed and saved: {_tagFile.FileName}"
                        : $"TAG file saved: {_tagFile.FileName}";

                    StatusLabel.Text = msg;
                    LogService.Info(msg);

                    // Refresh the file list
                    LoadTagFileList();
                }
                else
                {
                    MessageBox.Show(
                        $"Failed to save TAG file:\n\n{saveResult.GetSummary()}",
                        "Save Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to save TAG file", ex);
                MessageBox.Show(
                    $"Error saving TAG file:\n\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                SaveButton.IsEnabled = true;
                SaveButton.Content = "Save TAG";
                UpdateButtonStates();
            }
        }

        #endregion
    }
}
