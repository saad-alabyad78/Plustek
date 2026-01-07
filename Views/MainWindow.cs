using Plustek.ViewModels;
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

        private async void ScanButton_Click(object sender, System.Windows.RoutedEventArgs e) {
            await _viewModel.ScanAsync();
        }

        private void ResetButton_Click(object sender, System.Windows.RoutedEventArgs e) {
            _viewModel.Reset();
        }

        private void CancelButton_Click(object sender, System.Windows.RoutedEventArgs e) {
            Close();
        }
    }
}