using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Plustek.Services;
using Plustek.Models;

namespace Plustek {
    /// <summary>
    /// Main runner that integrates Plustek Scanner with Barcode Decoder
    /// </summary>
    public class PlustekBarcodeRunner {
        private readonly IPlustekScannerService _scanner;
        private readonly IOutputWriter _outputWriter;
        private readonly ILogger<PlustekBarcodeRunner> _logger;

        public PlustekBarcodeRunner(
            IPlustekScannerService scanner,
            IOutputWriter outputWriter,
            ILogger<PlustekBarcodeRunner> logger) {

            _scanner = scanner;
            _outputWriter = outputWriter;
            _logger = logger;
        }

        public async Task<int> RunInteractiveAsync() {
            Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║   PLUSTEK SCANNER + SYRIAN ID BARCODE DECODER             ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            try {
                // 1. Connect to Plustek server
                if (!await _scanner.ConnectAsync()) {
                    return 1;
                }

                // 2. Initialize scanner
                if (!await _scanner.InitializeAsync()) {
                    Console.WriteLine("❌ Failed to initialize scanner");
                    return 1;
                }

                // 3. Check device connection
                if (!await _scanner.IsDeviceConnectedAsync()) {
                    Console.WriteLine("❌ ERROR: No Plustek scanner detected!");
                    Console.WriteLine("   Please ensure:");
                    Console.WriteLine("   - Scanner is connected via USB");
                    Console.WriteLine("   - Scanner drivers are installed");
                    Console.WriteLine("   - PlustekSDK_LDSetup has been run");
                    return 1;
                }

                Console.WriteLine("✓ Scanner connected and ready");
                Console.WriteLine();

                // 4. Interactive scanning loop
                while (true) {
                    Console.WriteLine("═══════════════════════════════════════════════════════════");
                    Console.WriteLine("Place ID card on scanner and press ENTER to scan");
                    Console.WriteLine("Or type 'exit' to quit");
                    Console.Write("> ");

                    string? input = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(input)) {
                        // User pressed Enter - start scan
                        await ProcessScanAsync();
                    } else if (input.Trim().ToLower() == "exit") {
                        Console.WriteLine("\nExiting...");
                        break;
                    } else {
                        Console.WriteLine("Invalid input. Press ENTER to scan or type 'exit'.");
                    }
                }

                return 0;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Fatal error in runner");
                Console.WriteLine($"\n❌ FATAL ERROR: {ex.Message}");
                return 1;
            }
            finally {
                await _scanner.DisconnectAsync();
            }
        }

        private async Task ProcessScanAsync() {
            Console.WriteLine("\n🔄 Scanning...");

            try {
                // Prepare output path
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string tempDir = Path.Combine(Directory.GetCurrentDirectory(), "scans");
                Directory.CreateDirectory(tempDir);
                string scannedImagePath = Path.Combine(tempDir, $"scan_{timestamp}.jpg");

                // Scan and parse in one call - the service handles everything:
                // - Scans the document
                // - Finds the newest file in temp folder
                // - Copies it to output path
                // - Detects and decodes barcode
                // - Parses Syrian ID data
                // - Saves .txt file
                // - Returns parsed data
                SyrianIdData? idData = await _scanner.ScanAndParseAsync(scannedImagePath);

                if (idData == null) {
                    Console.WriteLine("❌ Failed to process ID card");
                    Console.WriteLine("   The scan or barcode detection failed.");
                    Console.WriteLine("   Please try again with better card positioning.");
                    return;
                }

                // Display summary results
                Console.WriteLine("\n" + new string('═', 60));
                Console.WriteLine("SYRIAN NATIONAL ID - DECODED INFORMATION");
                Console.WriteLine(new string('═', 60));
                Console.WriteLine($"National ID:      {idData.NationalId ?? "N/A"}");
                Console.WriteLine($"Name (Arabic):    {idData.FullNameArabic ?? "N/A"}");
                Console.WriteLine($"Name (English):   {idData.FullNameEnglish ?? "N/A"}");
                Console.WriteLine($"Date of Birth:    {idData.DateOfBirth?.ToString("dd-MM-yyyy") ?? "N/A"}");
                Console.WriteLine($"Gender:           {idData.Gender ?? "N/A"}");
                Console.WriteLine($"Address:          {idData.AddressArabic ?? "N/A"}");
                Console.WriteLine($"Issue Date:       {idData.IssueDate?.ToString("dd-MM-yyyy") ?? "N/A"}");
                Console.WriteLine($"Expiry Date:      {idData.ExpiryDate?.ToString("dd-MM-yyyy") ?? "N/A"}");
                Console.WriteLine(new string('═', 60));

                // Save additional output formats (HTML/JSON)
                await _outputWriter.SaveToFileAsync(idData, scannedImagePath);

                Console.WriteLine("\n✓ Complete results saved to HTML/JSON/TXT files");
                Console.WriteLine();

            }
            catch (Exception ex) {
                _logger.LogError(ex, "Error during scan processing");
                Console.WriteLine($"\n❌ Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Single scan mode - scan once and exit
        /// </summary>
        public async Task<int> RunSingleScanAsync(string? outputPath = null) {
            Console.WriteLine("Starting single scan mode...");

            if (!await _scanner.ConnectAsync()) return 1;
            if (!await _scanner.InitializeAsync()) return 1;
            if (!await _scanner.IsDeviceConnectedAsync()) {
                Console.WriteLine("❌ No scanner detected");
                return 1;
            }

            await ProcessScanAsync();
            await _scanner.DisconnectAsync();

            return 0;
        }
    }
}