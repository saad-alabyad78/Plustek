// File: Plustek/Runners/ScannerRunner.cs
using BarcodeIdScan;
using Plustek.Configuration;
using Plustek.Interfaces;
using Plustek.Parsers;
using Plustek.Services;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Plustek.Runner {
    public class ScannerRunner {
        private readonly AppSettings _settings;
        private readonly IScanner _scanner;
        private readonly IBarcodeDecoder _barcodeDecoder;
        private readonly IOutputWriter _outputWriter;
        private readonly ExcelDatabaseService _excelDatabase;

        private string? _currentNationalId = null;
        private string? _backFacePath = null;
        private bool _backFaceComplete = false;

        public ScannerRunner(
            AppSettings settings,
            IScanner scanner,
            IBarcodeDecoder barcodeDecoder,
            IOutputWriter outputWriter,
            ExcelDatabaseService excelDatabase) {
            _settings = settings;
            _scanner = scanner;
            _barcodeDecoder = barcodeDecoder;
            _outputWriter = outputWriter;
            _excelDatabase = excelDatabase;
        }

        public async Task<int> RunInteractiveAsync() {
            Console.WriteLine("╔════════════════════════════════════════════════╗");
            Console.WriteLine("║   PLUSTEK SYRIAN ID SCANNER                   ║");
            Console.WriteLine("╚════════════════════════════════════════════════╝\n");

            if (!await InitializeAsync()) {
                return 1;
            }

            try {
                Console.WriteLine("\n📋 Scan Order: 1) BACK face (with barcode), 2) FRONT face\n");

                while (true) {
                    Console.WriteLine("\nPress ENTER to scan or type 'exit' to quit");
                    Console.Write("> ");

                    string? input = Console.ReadLine();

                    if (string.IsNullOrWhiteSpace(input)) {
                        await ProcessScanAsync();
                    } else if (input.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase)) {
                        break;
                    } else if (input.Trim().Equals("reset", StringComparison.OrdinalIgnoreCase)) {
                        ResetSession();
                    }
                }

                return 0;
            }
            finally {
                // await _scanner.DisconnectAsync();
            }
        }

        public async Task<int> RunSingleScanAsync() {
            Console.WriteLine("Single scan mode\n");

            if (!await InitializeAsync()) {
                return 1;
            }

            try {
                await ProcessScanAsync();
                return 0;
            }
            finally {
                // await _scanner.DisconnectAsync();
            }
        }

        public async Task<int> TestImageAsync(string imagePath) {
            Console.WriteLine($"Testing image: {imagePath}\n");

            if (!File.Exists(imagePath)) {
                Console.WriteLine("❌ File not found");
                return 1;
            }

            var barcode = await _barcodeDecoder.ReadAsync(imagePath);

            if (barcode == null) {
                Console.WriteLine("❌ No barcode found, trying with enhancement...");
                barcode = await _barcodeDecoder.ReadBarcodeWithEnhancementAsync(
                    imagePath: imagePath,
                    enhancements: new[] { EnhancementTechnique.Sharpening }
                );
            }

            if (barcode == null) {
                Console.WriteLine("❌ No barcode found even after enhancement");
                return 1;
            }

            Console.WriteLine($"✓ Barcode found ({barcode.Length} chars)");

            if (string.IsNullOrEmpty(barcode.Text)) {
                Console.WriteLine("❌ Barcode text is empty");
                return 1;
            }

            var idData = SyrianIdParser.Parse(barcode.Text);
            if (idData == null) {
                Console.WriteLine("❌ Parse failed");
                return 1;
            }

            DisplayResults(idData);
            await _outputWriter.SaveAsync(idData, imagePath);

            return 0;
        }

        private async Task<bool> InitializeAsync() {
            try {
                Console.Write("Connecting to scanner... ");

                if (!await _scanner.ConnectAsync()) {
                    Console.WriteLine("✗ Failed");
                    Console.WriteLine("\nTroubleshooting:");
                    Console.WriteLine("  - Is Plustek SDK service running?");
                    Console.WriteLine("  - Check: netstat -an | findstr 17778");
                    return false;
                }
                Console.WriteLine("✓");

                Console.Write("Initializing... ");

                if (!await _scanner.InitializeAsync()) {
                    Console.WriteLine("✗ Failed");
                    Console.WriteLine("\nScanner initialization failed.");
                    Console.WriteLine("The SDK may be busy or not responding.");
                    return false;
                }
                Console.WriteLine("✓");

                return true;
            }
            catch (Exception ex) {
                Console.WriteLine($"\n❌ Error: {ex.Message}");
                return false;
            }
        }

        private async Task ProcessScanAsync() {
            Console.WriteLine("\n📸 Scanning...");

            string outputPath = _settings.GenerateOutputPath();
            var scanResult = await _scanner.ScanAsync(outputPath);

            if (scanResult == null || !scanResult.Success) {
                Console.WriteLine($"❌ Scan failed: {scanResult?.Error ?? "Unknown error"}");
                return;
            }

            Console.WriteLine("✓ Scan complete");
            Console.Write("🔍 Reading barcode... ");

            var barcode = await _barcodeDecoder.ReadAsync(outputPath);

            if (barcode == null) {
                Console.Write("Trying with enhancement... ");
                barcode = await _barcodeDecoder.ReadBarcodeWithEnhancementAsync(
                    imagePath: outputPath,
                    enhancements: new[] { EnhancementTechnique.Sharpening }
                );
            }

            if (!_backFaceComplete) {
                // STEP 1: We need the BACK face with barcode first
                if (barcode == null) {
                    Console.WriteLine("❌ No barcode detected");
                    Console.WriteLine("⚠️  Please scan the BACK face of the ID card (the side with the barcode).");

                    // Clean up temp file
                    if (File.Exists(outputPath)) {
                        try { File.Delete(outputPath); } catch { }
                    }
                    return;
                }

                // Barcode found - this is the BACK face
                Console.WriteLine("✓ Barcode detected - Back face");
                await HandleBackFaceAsync(outputPath, barcode.Text);
            } else {
                // STEP 2: We have back face, now get the FRONT face
                if (barcode != null) {
                    Console.WriteLine("❌ Barcode detected");
                    Console.WriteLine("⚠️  This appears to be the back face. Please scan the FRONT face (without barcode).");

                    // Clean up temp file
                    if (File.Exists(outputPath)) {
                        try { File.Delete(outputPath); } catch { }
                    }
                    return;
                }

                // No barcode = FRONT face
                Console.WriteLine("✓ No barcode - Front face detected");
                await HandleFrontFaceAsync(outputPath);
            }
        }

        private async Task HandleBackFaceAsync(string tempPath, string? barcodeText) {
            if (string.IsNullOrEmpty(barcodeText)) {
                Console.WriteLine("❌ Barcode text is empty");
                return;
            }

            Console.Write("📋 Parsing data... ");

            var idData = SyrianIdParser.Parse(barcodeText);

            if (idData == null) {
                Console.WriteLine("❌ Parse failed");
                Console.WriteLine("Barcode was read but could not be parsed as Syrian ID");

                string backFacePath = Path.Combine(_settings.OutputDirectory, $"back_face_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
                if (File.Exists(tempPath)) {
                    File.Move(tempPath, backFacePath, overwrite: true);
                }

                _backFacePath = backFacePath;
                _backFaceComplete = true;

                Console.WriteLine($"\n✓ Back face saved to: {backFacePath}");
                Console.WriteLine("\nℹ️  Now scan the FRONT face of the ID card.");
                return;
            }

            Console.WriteLine("✓");

            // Extract National ID
            string nationalId = idData.NationalId;

            if (string.IsNullOrEmpty(nationalId) || nationalId == "Unknown") {
                Console.WriteLine("⚠️  Could not extract National ID number");

                string backFacePath = Path.Combine(_settings.OutputDirectory, $"back_face_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
                if (File.Exists(tempPath)) {
                    File.Move(tempPath, backFacePath, overwrite: true);
                }

                _backFacePath = backFacePath;
                _backFaceComplete = true;

                Console.WriteLine($"\n✓ Back face saved to: {backFacePath}");
                Console.WriteLine("\nℹ️  Now scan the FRONT face of the ID card.");
                return;
            }

            _currentNationalId = nationalId;
            _backFaceComplete = true;

            DisplayResults(idData);

            // Save back face
            string backPath = _settings.GetBackFacePath(nationalId);
            if (File.Exists(tempPath)) {
                File.Move(tempPath, backPath, overwrite: true);
            }
            _backFacePath = backPath;

            // Save outputs
            await _outputWriter.SaveAsync(idData, backPath);

            // Save to Excel database
            await _excelDatabase.SaveRecordAsync(idData, null, backPath);

            Console.WriteLine($"\n✓ Back face and data saved to: {Path.GetDirectoryName(backPath)}");
            Console.WriteLine($"📋 National ID: {nationalId}");
            Console.WriteLine("\nℹ️  Now scan the FRONT face of the ID card.");
        }

        private async Task HandleFrontFaceAsync(string tempPath) {
            if (string.IsNullOrEmpty(_currentNationalId)) {
                Console.WriteLine("❌ Error: Back face must be scanned first");

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

            // Update Excel database with front face path
            var idData = new Models.SyrianIdData {
                Fields = new System.Collections.Generic.List<string> {
                    "", "", "", "", "", _currentNationalId ?? ""
                }
            };
            await _excelDatabase.SaveRecordAsync(idData, frontPath, _backFacePath);

            Console.WriteLine($"\n✓ Front face saved to: {Path.GetDirectoryName(frontPath)}");
            Console.WriteLine($"\n✅ COMPLETE! Both faces saved for National ID: {_currentNationalId}");
            Console.WriteLine($"   📂 Folder: {Path.GetDirectoryName(frontPath)}");

            var recordCount = await _excelDatabase.GetRecordCountAsync();
            Console.WriteLine($"   📊 Total records in database: {recordCount}");

            // Reset for next scan
            ResetSession();
            Console.WriteLine("\n🔄 Ready for next ID card. Scan the BACK face first.");
        }

        private void ResetSession() {
            _currentNationalId = null;
            _backFacePath = null;
            _backFaceComplete = false;
        }

        private void DisplayResults(Models.SyrianIdData data) {
            Console.WriteLine("\n" + new string('═', 60));
            Console.WriteLine("SYRIAN ID CARD - ALL FIELDS");
            Console.WriteLine(new string('═', 60));
            Console.WriteLine($"Total Fields: {data.Fields.Count}");
            Console.WriteLine();

            for (int i = 0; i < data.Fields.Count; i++) {
                Console.WriteLine($"Field [{i:D2}]: {data.Fields[i]}");
            }

            Console.WriteLine(new string('═', 60));
        }
    }
}