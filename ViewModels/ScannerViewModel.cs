using BarcodeIdScan;
using Plustek.Configuration;
using Plustek.Interfaces;
using Plustek.Parsers;
using Plustek.Services;
using System;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;

namespace Plustek.ViewModels {
    public class ScannerViewModel : INotifyPropertyChanged {
        private readonly AppSettings _settings;
        private readonly IScanner _scanner;
        private readonly IBarcodeDecoder _barcodeDecoder;
        private readonly ExcelDatabaseService _excelDatabase;

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
        private bool _backFaceComplete = false; // Track if back face is scanned

        public ScannerViewModel(
            AppSettings settings,
            IScanner scanner,
            IBarcodeDecoder barcodeDecoder,
            ExcelDatabaseService excelDatabase) {
            _settings = settings;
            _scanner = scanner;
            _barcodeDecoder = barcodeDecoder;
            _excelDatabase = excelDatabase;
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

                // Verify device authorization
                ShowStatus("Verifying device authorization...", WpfBrushes.Orange);
                string? serialNumber = await _scanner.GetDeviceSerialNumberAsync();

                if (string.IsNullOrEmpty(serialNumber)) {
                    ShowStatus("❌ Could not retrieve device serial number.", WpfBrushes.Red);
                    return;
                }

                // Check if serial number matches the authorized device
                const string AUTHORIZED_SERIAL = "kj01010f6001115";

                if (serialNumber != AUTHORIZED_SERIAL) {
                    ShowStatus($"❌ UNAUTHORIZED DEVICE", WpfBrushes.Red);
                    /* MessageBox.Show(
                         $"This device is not authorized to use this application.\n\n" +
                         $"Device Serial: {serialNumber}\n" +
                         $"Expected Serial: {AUTHORIZED_SERIAL}",
                         "Unauthorized Device",
                         MessageBoxButton.OK,
                         MessageBoxImage.Error
                     );*/
                    return;
                }

                IsScanEnabled = true;
                ShowStatus($"✓ Scanner ready . Please scan the BACK face first (with barcode).", WpfBrushes.Green);
            }
            catch (Exception ex) {
                ShowStatus($"❌ Error: {ex.Message}", WpfBrushes.Red);
                MessageBox.Show($"Error initializing scanner:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

                if (!_backFaceComplete) {
                    // STEP 1: We need the BACK face with barcode first
                    ShowStatus("🔍 Reading barcode...", WpfBrushes.Orange);

                    var barcode = await _barcodeDecoder.ReadAsync(tempPath);

                    // Try enhancement if first attempt fails
                    if (barcode == null) {
                        ShowStatus("🔍 No barcode detected, trying with enhancement...", WpfBrushes.Orange);
                        barcode = await _barcodeDecoder.ReadBarcodeWithEnhancementAsync(
                            imagePath: tempPath,
                            enhancements: new[] { EnhancementTechnique.Sharpening }
                        );

                        if (barcode != null) {
                            ShowStatus("✓ Barcode detected with enhancement!", WpfBrushes.Green);
                        }
                    }

                    if (barcode == null) {
                        // No barcode found - wrong side or error
                        ShowStatus("❌ No barcode detected. Please scan the BACK face (with barcode).", WpfBrushes.Red);

                        // Clean up temp file
                        if (File.Exists(tempPath)) {
                            try { File.Delete(tempPath); } catch { }
                        }
                    } else {
                        // Barcode found - this is the BACK face
                        await HandleBackFaceAsync(tempPath, barcode.Text);
                    }
                } else {
                    // STEP 2: We have back face, now get the FRONT face
                    // NO BARCODE CHECK - just accept the scan as front face
                    // User can press Reset button if they made a mistake
                    ShowStatus("✓ Processing front face...", WpfBrushes.Orange);
                    await HandleFrontFaceAsync(tempPath);
                }
            }
            catch (Exception ex) {
                ShowStatus($"❌ Error: {ex.Message}", WpfBrushes.Red);
            }
            finally {
                IsScanEnabled = true;
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
                _backFaceComplete = true;

                ShowStatus("⚠ Back face saved but data parsing failed. Please scan the FRONT face.", WpfBrushes.Orange);
                return;
            }

            // Extract National ID
            string nationalId = idData.Fields.Count > 5 ? idData.Fields[5].Trim() : "Unknown";

            if (string.IsNullOrEmpty(nationalId) || nationalId == "Unknown") {
                ShowStatus("❌ Could not extract National ID number.", WpfBrushes.Red);
                _backFacePath = tempPath;
                BackFaceImage = LoadImage(tempPath);
                _backFaceComplete = true;

                ShowStatus("⚠ Back face saved but no National ID found. Please scan the FRONT face.", WpfBrushes.Orange);
                return;
            }

            _currentNationalId = nationalId;
            _backFaceComplete = true;

            // Save back face
            string backPath = _settings.GetBackFacePath(nationalId);
            if (File.Exists(tempPath)) {
                File.Move(tempPath, backPath, overwrite: true);
            }
            _backFacePath = backPath;
            BackFaceImage = LoadImage(backPath);

            // Extract and populate individual fields
            NationalId = nationalId;
            FirstName = idData.Fields.Count > 0 ? idData.Fields[0] : "";
            FatherName = idData.Fields.Count > 1 ? idData.Fields[2] : "";
            MotherName = idData.Fields.Count > 2 ? idData.Fields[3] : "";
            LastName = idData.Fields.Count > 3 ? idData.Fields[1] : "";
            BirthInfo = idData.Fields.Count > 4 ? idData.Fields[4] : "";
            ScannedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            HasData = true;


            // Save to Excel database
            await _excelDatabase.SaveRecordAsync(idData, null, backPath);

            ShowStatus($"✓ Back face and data saved. National ID: {nationalId}. Now scan the FRONT face.", WpfBrushes.Blue);
        }

        private async Task HandleFrontFaceAsync(string tempPath) {
            if (string.IsNullOrEmpty(_currentNationalId)) {
                // This shouldn't happen in the new flow, but handle it
                ShowStatus("❌ Error: Back face must be scanned first.", WpfBrushes.Red);

                if (File.Exists(tempPath)) {
                    try { File.Delete(tempPath); } catch { }
                }
                return;
            }

            // Save front face with the National ID we already have
            string frontPath = _settings.GetFrontFacePath(_currentNationalId);

            if (File.Exists(tempPath)) {
                File.Move(tempPath, frontPath, overwrite: true);
            }

            _frontFacePath = frontPath;
            FrontFaceImage = LoadImage(frontPath);

            // Update database with front face path
            if (HasData) {
                var idData = new Models.SyrianIdData {
                    Fields = new System.Collections.Generic.List<string> {
                        FirstName ?? "",
                        LastName ?? "",
                        FatherName ?? "",
                        MotherName ?? "",
                        BirthInfo ?? "",
                        NationalId ?? ""
                    }
                };
                await _excelDatabase.SaveRecordAsync(idData, frontPath, _backFacePath);
            }

            ShowStatus($"✓ Complete! Both faces saved. National ID: {_currentNationalId}", WpfBrushes.Green);
        }

        public async Task<string?> ExportDatabaseAsync() {
            try {
                // Show Save File Dialog
                var saveFileDialog = new SaveFileDialog {
                    Title = "Export Database and Images",
                    Filter = "ZIP Archive (*.zip)|*.zip",
                    FileName = $"SyrianID_Export_{DateTime.Now:yyyyMMdd_HHmmss}.zip",
                    DefaultExt = "zip"
                };

                if (saveFileDialog.ShowDialog() != true) {
                    ShowStatus("Export cancelled.", WpfBrushes.Gray);
                    return null;
                }

                string exportPath = saveFileDialog.FileName;

                ShowStatus("📤 Exporting database and images...", WpfBrushes.Orange);

                await Task.Run(() => {
                    // Get the database directory
                    string databaseDir = Path.GetDirectoryName(_excelDatabase.GetDatabasePath()) ?? _settings.OutputDirectory;

                    // Create the ZIP file
                    if (File.Exists(exportPath)) {
                        File.Delete(exportPath);
                    }

                    using (var archive = ZipFile.Open(exportPath, ZipArchiveMode.Create)) {
                        // Add the database file
                        string dbPath = _excelDatabase.GetDatabasePath();
                        if (File.Exists(dbPath)) {
                            archive.CreateEntryFromFile(dbPath, Path.GetFileName(dbPath));
                        }

                        // Add all folders and their contents in the same directory as the database
                        var directories = Directory.GetDirectories(databaseDir);

                        foreach (var dir in directories) {
                            var dirInfo = new DirectoryInfo(dir);

                            // Get all files in this directory and subdirectories
                            var files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories);

                            foreach (var file in files) {
                                // Create relative path for the archive
                                string relativePath = Path.GetRelativePath(databaseDir, file);
                                archive.CreateEntryFromFile(file, relativePath);
                            }
                        }
                    }
                });

                var recordCount = await _excelDatabase.GetRecordCountAsync();
                ShowStatus($"✓ Export complete! {recordCount} records and images saved to: {Path.GetFileName(exportPath)}", WpfBrushes.Green);

         /*       // Ask if user wants to open the folder
                var result = MessageBox.Show(
                    $"Export successful!\n\nFile saved to:\n{exportPath}\n\nDo you want to open the folder?",
                    "Export Complete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information
                );

                if (result == MessageBoxResult.Yes) {
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{exportPath}\"");
                }*/

                return exportPath;
            }
            catch (Exception ex) {
                ShowStatus($"❌ Export failed: {ex.Message}", WpfBrushes.Red);
                MessageBox.Show($"Export failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        public async Task<string> GetDatabaseInfoAsync() {
            var recordCount = await _excelDatabase.GetRecordCountAsync();
            var dbPath = _excelDatabase.GetDatabasePath();
            return $"{recordCount} records in database\n{dbPath}";
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
            _backFaceComplete = false;
            ShowStatus("🔄 Reset complete. Please scan the BACK face first (with barcode).", WpfBrushes.Gray);
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