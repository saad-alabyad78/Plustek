// File: Plustek/Views/MainWindow.xaml.cs
using Plustek.ViewModels;
using System.Diagnostics;
using System.IO;
using System.Windows;
using WpfWindow = System.Windows.Window;

namespace Plustek.Views {
    public partial class MainWindow : WpfWindow {
        private readonly ScannerViewModel _viewModel;

        public MainWindow(ScannerViewModel viewModel) {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = _viewModel;

            Loaded += async (s, e) => await _viewModel.InitializeAsync();
            Closing += async (s, e) => await _viewModel.CleanupAsync();
        }

        private async void ScanButton_Click(object sender, RoutedEventArgs e) {
            await _viewModel.ScanAsync();
        }

        private async void ExportButton_Click(object sender, RoutedEventArgs e) {
            var exportPath = await _viewModel.ExportDatabaseAsync();

            if (exportPath != null && File.Exists(exportPath)) {
                var result = MessageBox.Show(
                    $"Database exported successfully!\n\n{exportPath}\n\nWould you like to open the file?",
                    "Export Complete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information
                );

                if (result == MessageBoxResult.Yes) {
                    try {
                        Process.Start(new ProcessStartInfo {
                            FileName = exportPath,
                            UseShellExecute = true
                        });
                    }
                    catch {
                        MessageBox.Show(
                            "Could not open the file. Please open it manually from:\n" + exportPath,
                            "Information",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information
                        );
                    }
                }
            }
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e) {
            _viewModel.Reset();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) {
            Close();
        }
    }
}