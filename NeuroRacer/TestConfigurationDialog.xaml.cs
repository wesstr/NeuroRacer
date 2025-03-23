using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace NeuroRacer
{
    public partial class TestConfigurationDialog : Window
    {
        // These properties will hold the selected file paths.
        public string ScheduleFilePath { get; private set; }
        public string CsvLogFile { get; private set; }

        public TestConfigurationDialog()
        {
            InitializeComponent();
        }

        private void SelectScheduleButton_Click(object sender, RoutedEventArgs e)
        {
            // Use OpenFileDialog for schedule file selection.
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "JSON Files (*.json)|*.json",
                Title = "Select Test Schedule File"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                ScheduleFilePath = openFileDialog.FileName;
                MessageBox.Show("Schedule file selected:\n" + ScheduleFilePath, "Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                ValidateSelections();
            }
        }

        private void SelectOutputButton_Click(object sender, RoutedEventArgs e)
        {
            // Pre-populate a default file name with a timestamp.
            string defaultFileName = $"TestResults_{DateTime.Now:yyyyMMdd_HHmmss}.csv";

            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv",
                Title = "Select Output CSV File",
                FileName = defaultFileName
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                CsvLogFile = saveFileDialog.FileName;
                MessageBox.Show("Output CSV file selected:\n" + CsvLogFile, "Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                ValidateSelections();
            }
        }

        /// <summary>
        /// Enables the OK button only if both ScheduleFilePath and CsvLogFile have been set.
        /// </summary>
        private void ValidateSelections()
        {
            OkButton.IsEnabled = !string.IsNullOrWhiteSpace(ScheduleFilePath) &&
                                   !string.IsNullOrWhiteSpace(CsvLogFile);
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // Final check (although the button shouldn't be clickable unless both are valid).
            if (string.IsNullOrEmpty(ScheduleFilePath))
            {
                MessageBox.Show("Please select a schedule file.", "Missing Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrEmpty(CsvLogFile))
            {
                MessageBox.Show("Please select an output CSV file.", "Missing Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
            Close();
        }
    }
}
