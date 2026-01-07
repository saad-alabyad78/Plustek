using System;
using System.IO;
using System.Threading.Tasks;
using Plustek.Configuration;
using Plustek.Interfaces;
using Plustek.Models;
using Plustek.Parsers;

namespace Plustek.Runner {
    public class ScannerRunner {
        private readonly AppSettings _settings;
        private readonly IScanner _scanner;
        private readonly IBarcodeDecoder _barcodeReader;
        private readonly IOutputWriter _outputWriter;

        public ScannerRunner(
            AppSettings settings,
            IScanner scanner,
            IBarcodeDecoder barcodeReader,
            IOutputWriter outputWriter) {
            _settings = settings;
            _scanner = scanner;
            _barcodeReader = barcodeReader;
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
                await _scanner.DisconnectAsync();
            }
        }

        public async Task<int> TestImageAsync(string imagePath) {
            Console.WriteLine($"Testing image: {imagePath}\n");

            if (!File.Exists(imagePath)) {
                Console.WriteLine("❌ File not found");
                return 1;
            }

            var barcode = await _barcodeReader.ReadAsync(imagePath);
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
            Console.Write("Connecting to scanner... ");

            if (!await _scanner.ConnectAsync()) {
                Console.WriteLine("❌ Failed");
                return false;
            }
            Console.WriteLine("✓");

            Console.Write("Initializing... ");
            if (!await _scanner.InitializeAsync()) {
                Console.WriteLine("❌ Failed");
                return false;
            }
            Console.WriteLine("✓");

            return true;
        }

        private async Task ProcessScanAsync() {
            Console.WriteLine("\n📸 Scanning...");

            string outputPath = _settings.GenerateOutputPath();
            var scanResult = await _scanner.ScanAsync(outputPath);

            if (scanResult == null || !scanResult.Success) {
                Console.WriteLine($"❌ Scan failed: {scanResult?.Error}");
                return;
            }

            Console.WriteLine("✓ Scan complete");
            Console.Write("🔍 Reading barcode... ");

            var barcode = await _barcodeReader.ReadAsync(outputPath);
            if (barcode == null) {
                Console.WriteLine("❌ No barcode found");
                return;
            }
            Console.WriteLine("✓");

            Console.Write("📋 Parsing data... ");
            var idData = SyrianIdParser.Parse(barcode.Text);
            if (idData == null) {
                Console.WriteLine("❌ Parse failed");
                return;
            }
            Console.WriteLine("✓");

            DisplayResults(idData);
            await _outputWriter.SaveAsync(idData, outputPath);

            Console.WriteLine($"\n✓ Files saved to: {_settings.OutputDirectory}");
        }

        private void DisplayResults(Models.SyrianIdData data) {
            Console.WriteLine("\n" + new string('═', 50));
            Console.WriteLine("SYRIAN NATIONAL ID");
            Console.WriteLine(new string('═', 50));
            Console.WriteLine($"National ID:      {data.NationalId ?? "N/A"}");
            Console.WriteLine($"Name (Arabic):    {data.FullNameArabic ?? "N/A"}");
            Console.WriteLine($"Name (English):   {data.FullNameEnglish ?? "N/A"}");
            Console.WriteLine($"Date of Birth:    {data.DateOfBirth?.ToString("dd-MM-yyyy") ?? "N/A"}");
            Console.WriteLine($"Gender:           {data.Gender ?? "N/A"}");
            Console.WriteLine($"Address:          {data.AddressArabic ?? "N/A"}");
            Console.WriteLine(new string('═', 50));
        }
    }
}