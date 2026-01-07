using Plustek.Configuration;
using Plustek.Interfaces;
using Plustek.Parsers;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;

namespace Plustek.ViewModels {
    public class ScannerViewModel : INotifyPropertyChanged {
        private readonly AppSettings _settings;
        private readonly IScanner _scanner;
        private readonly IBarcodeDecoder _barcodeDecoder;
        private readonly IOutputWriter _outputWriter;

        private BitmapImage? _frontFaceImage;
        private BitmapImage? _backFaceImage;
        private string? _nationalId;
        private string? _firstName;
        private string? _fatherName;
        private string? _motherName;
        private string? _lastName;
        private string? _birthInfo;
        private string? _scannedAt;
        private bool _hasData = false;
        private string _statusMessage = "";
        private bool _isScanEnabled = false;
        private WpfBrush _statusBackground = WpfBrushes.Transparent;
        private Visibility _statusVisible = Visibility.Collapsed;
        private string? _currentNationalId;
        private string? _frontFacePath;
        private string? _backFacePath;

        public ScannerViewModel(
            AppSettings settings,
            IScanner scanner,
            IBarcodeDecoder barcodeDecoder,
            IOutputWriter outputWriter) {
            _settings = settings;
            _scanner = scanner;
            _barcodeDecoder = barcodeDecoder;
            _outputWriter = outputWriter;
        }

        // Properties
        public BitmapImage? FrontFaceImage {
            get => _frontFaceImage;
            set {
                _frontFaceImage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FrontFacePlaceholderVisible));
            }
        }

        public BitmapImage? BackFaceImage {
            get => _backFaceImage;
            set {
                _backFaceImage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BackFacePlaceholderVisible));
            }
        }

        public string? NationalId {
            get => _nationalId;
            set {
                _nationalId = value;
                OnPropertyChanged();
            }
        }

        public string? FirstName {
            get => _firstName;
            set {
                _firstName = value;
                OnPropertyChanged();
            }
        }

        public string? FatherName {
            get => _fatherName;
            set {
                _fatherName = value;
                OnPropertyChanged();
            }
        }

        public string? MotherName {
            get => _motherName;
            set {
                _motherName = value;
                OnPropertyChanged();
            }
        }

        public string? LastName {
            get => _lastName;
            set {
                _lastName = value;
                OnPropertyChanged();
            }
        }

        public string? BirthInfo {
            get => _birthInfo;
            set {
                _birthInfo = value;
                OnPropertyChanged();
            }
        }

        public string? ScannedAt {
            get => _scannedAt;
            set {
                _scannedAt = value;
                OnPropertyChanged();
            }
        }

        public bool HasData {
            get => _hasData;
            set {
                _hasData = value;
                OnPropertyChanged();
            }
        }

        public string StatusMessage {
            get => _statusMessage;
            set {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        public WpfBrush StatusBackground {
            get => _statusBackground;
            set {
                _statusBackground = value;
                OnPropertyChanged();
            }
        }

        public Visibility StatusVisible {
            get => _statusVisible;
            set {
                _statusVisible = value;
                OnPropertyChanged();
            }
        }

        public bool IsScanEnabled {
            get => _isScanEnabled;
            set {
                _isScanEnabled = value;
                OnPropertyChanged();
            }
        }

        public Visibility FrontFacePlaceholderVisible =>
            FrontFaceImage is null ? Visibility.Visible : Visibility.Collapsed;

        public Visibility BackFacePlaceholderVisible =>
            BackFaceImage is null ? Visibility.Visible : Visibility.Collapsed;

        // Methods
        public async Task InitializeAsync() {
            ShowStatus("Connecting to scanner...", WpfBrushes.Orange);

            try {
                if (!await _scanner.ConnectAsync()) {
                    ShowStatus("❌ Failed to connect to scanner. Is the Plustek SDK service running?", WpfBrushes.Red);
                    return;
                }

                if (!await _scanner.InitializeAsync()) {
                    ShowStatus("❌ Scanner initialization failed. The SDK may be busy.", WpfBrushes.Red);
                    return;
                }

                IsScanEnabled = true;
                ShowStatus("✓ Scanner ready. Press SCAN to begin.", WpfBrushes.Green);
            }
            catch (Exception ex) {
                ShowStatus($"❌ Error: {ex.Message}", WpfBrushes.Red);
            }
        }

        public async Task ScanAsync() {
            if (!IsScanEnabled) return;

            IsScanEnabled = false;
            ShowStatus("📸 Scanning... Please wait.", WpfBrushes.Orange);

            try {
                // Scan the image
                string tempPath = _settings.GenerateOutputPath();
                var scanResult = await _scanner.ScanAsync(tempPath);

                if (scanResult == null || !scanResult.Success) {
                    ShowStatus($"❌ Scan failed: {scanResult?.Error ?? "Unknown error"}", WpfBrushes.Red);
                    IsScanEnabled = true;
                    return;
                }

                // Try to read barcode
                var barcode = await _barcodeDecoder.ReadAsync(tempPath);

                if (barcode == null) {
                    // No barcode = FRONT FACE
                    HandleFrontFaceAsync(tempPath);
                } else {
                    // Barcode found = BACK FACE
                    await HandleBackFaceAsync(tempPath, barcode.Text);
                }
            }
            catch (Exception ex) {
                ShowStatus($"❌ Error: {ex.Message}", WpfBrushes.Red);
            }
            finally {
                IsScanEnabled = true;
            }
        }

        private void HandleFrontFaceAsync(string tempPath) {
            if (string.IsNullOrEmpty(_currentNationalId)) {
                // No National ID yet, just store temporarily
                _frontFacePath = tempPath;
                FrontFaceImage = LoadImage(tempPath);
                ShowStatus("✓ Front face scanned. Please scan the BACK face to extract data.", WpfBrushes.Blue);
            } else {
                // We have National ID, save properly
                string frontPath = _settings.GetFrontFacePath(_currentNationalId);

                if (File.Exists(tempPath)) {
                    File.Move(tempPath, frontPath, overwrite: true);
                }

                _frontFacePath = frontPath;
                FrontFaceImage = LoadImage(frontPath);

                if (string.IsNullOrEmpty(_backFacePath)) {
                    ShowStatus("✓ Front face saved. Please scan the BACK face to extract data.", WpfBrushes.Blue);
                } else {
                    ShowStatus("✓ Front face saved. Both faces complete!", WpfBrushes.Green);
                }
            }
        }

        private async Task HandleBackFaceAsync(string tempPath, string barcodeText) {
            ShowStatus("🔍 Processing barcode data...", WpfBrushes.Orange);

            // Parse the barcode
            var idData = SyrianIdParser.Parse(barcodeText);

            if (idData == null) {
                ShowStatus("❌ Failed to parse Syrian ID data from barcode.", WpfBrushes.Red);

                // Still save as back face
                _backFacePath = tempPath;
                BackFaceImage = LoadImage(tempPath);
                return;
            }

            // Extract National ID
            string nationalId = idData.Fields.Count > 5 ? idData.Fields[5].Trim() : "Unknown";

            if (string.IsNullOrEmpty(nationalId) || nationalId == "Unknown") {
                ShowStatus("❌ Could not extract National ID number.", WpfBrushes.Red);
                _backFacePath = tempPath;
                BackFaceImage = LoadImage(tempPath);
                return;
            }

            _currentNationalId = nationalId;

            // Save back face
            string backPath = _settings.GetBackFacePath(nationalId);
            if (File.Exists(tempPath)) {
                File.Move(tempPath, backPath, overwrite: true);
            }
            _backFacePath = backPath;
            BackFaceImage = LoadImage(backPath);

            // Move front face if it exists
            if (!string.IsNullOrEmpty(_frontFacePath) && File.Exists(_frontFacePath)) {
                string frontPath = _settings.GetFrontFacePath(nationalId);
                if (_frontFacePath != frontPath) {
                    File.Move(_frontFacePath, frontPath, overwrite: true);
                    _frontFacePath = frontPath;
                    FrontFaceImage = LoadImage(frontPath);
                }
            }

            // Extract and populate individual fields
            NationalId = nationalId;
            FirstName = idData.Fields.Count > 0 ? idData.Fields[0] : "";
            FatherName = idData.Fields.Count > 1 ? idData.Fields[2] : "";
            MotherName = idData.Fields.Count > 2 ? idData.Fields[3] : "";
            LastName = idData.Fields.Count > 3 ? idData.Fields[1] : "";
            BirthInfo = idData.Fields.Count > 4 ? idData.Fields[4] : "";
            ScannedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            HasData = true;

            // Save outputs
            await _outputWriter.SaveAsync(idData, backPath);

            if (FrontFaceImage is null) {
                ShowStatus($"✓ Back face and data saved. National ID: {nationalId}. Please scan the FRONT face.", WpfBrushes.Blue);
            } else {
                ShowStatus($"✓ Complete! Both faces and data saved. National ID: {nationalId}", WpfBrushes.Green);
            }
        }

        public void Reset() {
            FrontFaceImage = null;
            BackFaceImage = null;
            NationalId = null;
            FirstName = null;
            FatherName = null;
            MotherName = null;
            LastName = null;
            BirthInfo = null;
            ScannedAt = null;
            HasData = false;
            _currentNationalId = null;
            _frontFacePath = null;
            _backFacePath = null;
            ShowStatus("🔄 Reset complete. Ready for new scan.", WpfBrushes.Gray);
        }

        public async Task CleanupAsync() {
            try {
                await _scanner.DisconnectAsync();
            }
            catch {
                // Ignore cleanup errors
            }
        }

        private BitmapImage? LoadImage(string path) {
            try {
                if (!File.Exists(path)) return null;

                // Wait a bit for file to be fully written
                System.Threading.Thread.Sleep(100);

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();

                return bitmap;
            }
            catch (Exception ex) {
                ShowStatus($"⚠ Warning: Could not load image - {ex.Message}", WpfBrushes.Orange);
                return null;
            }
        }

        private void ShowStatus(string message, WpfBrush background) {
            StatusMessage = message;
            StatusBackground = background;
            StatusVisible = Visibility.Visible;
        }

        // INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}