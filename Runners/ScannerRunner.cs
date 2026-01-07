using Plustek.Configuration;
using Plustek.Interfaces;
using Plustek.Parsers;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Plustek.Runner {
    public class ScannerRunner {
        private readonly AppSettings _settings;
        private readonly IScanner _scanner;
        private readonly IBarcodeDecoder _barcodeDecoder;
        private readonly IOutputWriter _outputWriter;

        public ScannerRunner(
            AppSettings settings,
            IScanner scanner,
            IBarcodeDecoder barcodeDecoder,
            IOutputWriter outputWriter) {
            _settings = settings;
            _scanner = scanner;
            _barcodeDecoder = barcodeDecoder;
            _outputWriter = outputWriter;
        }

        public async Task<int> RunInteractiveAsync() {
            Console.WriteLine("╔════════════════════════════════════════════════╗");
            Console.WriteLine("║   PLUSTEK SYRIAN ID SCANNER                   ║");
            Console.WriteLine("╚════════════════════════════════════════════════╝\n");

            if (!await InitializeAsync()) {
                return 1;
            }

            try {
                while (true) {
                    Console.WriteLine("\nPress ENTER to scan or type 'exit' to quit");
                    Console.Write("> ");

                    string? input = Console.ReadLine();

                    if (string.IsNullOrWhiteSpace(input)) {
                        await ProcessScanAsync();
                    } else if (input.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase)) {
                        break;
                    }
                }

                return 0;
            }
            finally {
                await _scanner.DisconnectAsync();
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
                Console.WriteLine("❌ No barcode found");
                return 1;
            }

            Console.WriteLine($"✓ Barcode found ({barcode.Length} chars)");

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
                // No barcode found - this is the FRONT face
                Console.WriteLine("✓ Front face detected (no barcode)");

                // We don't know the National ID yet, so save to temp location
                string frontFacePath = Path.Combine(_settings.OutputDirectory, $"front_face_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");

                if (File.Exists(outputPath)) {
                    File.Move(outputPath, frontFacePath, overwrite: true);
                }

                Console.WriteLine($"\n✓ Front face saved to: {frontFacePath}");
                Console.WriteLine("\nℹ️  Scan the back of the ID card next to extract data.");
                return;
            }

            // Barcode found - this is the BACK face
            Console.WriteLine("✓ Back face detected (barcode found)");

            Console.Write("📋 Parsing data... ");
            var idData = SyrianIdParser.Parse(barcode.Text);
            if (idData == null) {
                Console.WriteLine("❌ Parse failed");
                Console.WriteLine("Barcode was read but could not be parsed as Syrian ID");

                // Still save as back_face even if parsing fails
                string backFacePath = Path.Combine(_settings.OutputDirectory, $"back_face_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
                if (File.Exists(outputPath)) {
                    File.Move(outputPath, backFacePath, overwrite: true);
                }
                Console.WriteLine($"\n✓ Back face saved to: {backFacePath}");
                return;
            }
            Console.WriteLine("✓");

            DisplayResults(idData);

            try {
                // Extract National ID
                string nationalId = idData.Fields.Count > 5 ? idData.Fields[5].Trim() : "Unknown";

                if (!string.IsNullOrEmpty(nationalId) && nationalId != "Unknown") {
                    // Use the back_face path from AppSettings
                    string backFacePath = _settings.GetBackFacePath(nationalId);

                    // Move the scanned image to the National ID folder as back_face.jpg
                    if (File.Exists(outputPath)) {
                        File.Move(outputPath, backFacePath, overwrite: true);
                    }

                    // Save outputs to National ID folder
                    await _outputWriter.SaveAsync(idData, backFacePath);

                    Console.WriteLine($"\n✓ Back face and data saved to: {Path.GetDirectoryName(backFacePath)}");
                } else {
                    // Fallback: save to default location
                    string backFacePath = Path.Combine(_settings.OutputDirectory, $"back_face_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
                    if (File.Exists(outputPath)) {
                        File.Move(outputPath, backFacePath, overwrite: true);
                    }
                    await _outputWriter.SaveAsync(idData, backFacePath);
                    Console.WriteLine($"\n✓ Files saved to: {_settings.OutputDirectory}");
                }
            }
            catch (Exception ex) {
                Console.WriteLine($"\n⚠ Warning: Could not save output files: {ex.Message}");
            }
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