using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Plustek.Services;
using Plustek.Models;

namespace Plustek {
    /// <summary>
    /// Test class for Syrian ID barcode detection and parsing
    /// </summary>
    public class BarcodeDecoderTest {
        private readonly IImageLoader _imageLoader;
        private readonly IBarcodeDetector _detector;
        private readonly IOutputWriter _outputWriter;
        private readonly ILogger<BarcodeDecoderTest> _logger;

        public BarcodeDecoderTest(
            IImageLoader imageLoader,
            IBarcodeDetector detector,
            IOutputWriter outputWriter,
            ILogger<BarcodeDecoderTest> logger) {
            _imageLoader = imageLoader;
            _detector = detector;
            _outputWriter = outputWriter;
            _logger = logger;
        }

        /// <summary>
        /// Test barcode detection and parsing on a single image
        /// </summary>
        public async Task<bool> TestImageAsync(string imagePath) {
            Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║   SYRIAN ID BARCODE DECODER - TEST MODE                   ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
            Console.WriteLine();
            Console.WriteLine($"Testing image: {imagePath}");
            Console.WriteLine();

            // Load image
            var mat = await _imageLoader.LoadImageAsync(imagePath);
            if (mat == null || mat.Empty()) {
                _logger.LogError("Failed to load image or image empty.");
                Console.WriteLine("❌ Failed to load image or image is empty");
                return false;
            }

            Console.WriteLine($"✓ Image loaded: {mat.Width}x{mat.Height} pixels");
            Console.WriteLine();

            // Detect and decode barcode
            Console.WriteLine("🔍 Detecting barcode...");
            var rawBarcode = await _detector.DetectAndDecodeAsync(mat);

            if (string.IsNullOrEmpty(rawBarcode)) {
                _logger.LogWarning("No barcode could be decoded from the image.");
                Console.WriteLine("❌ No barcode detected/decoded.");
                Console.WriteLine();
                Console.WriteLine("Possible reasons:");
                Console.WriteLine("  - Image quality is too low");
                Console.WriteLine("  - Barcode is not visible or in focus");
                Console.WriteLine("  - Barcode is rotated or distorted");
                return false;
            }

            Console.WriteLine($"✓ Barcode detected! ({rawBarcode.Length} characters)");
            _logger.LogInformation("Raw barcode data length: {Length} characters", rawBarcode.Length);
            Console.WriteLine();

            // Parse Syrian ID data
            Console.WriteLine("📋 Parsing Syrian ID data...");
            try {
                var idData = SyrianIdParser.Parse(rawBarcode);

                // Display to console
                Console.WriteLine();
                Console.WriteLine(idData.ToString());

                // Save to HTML, JSON, and TXT files
                await _outputWriter.SaveToFileAsync(idData, imagePath);

                Console.WriteLine();
                Console.WriteLine("✓ Test completed successfully!");
                return true;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to parse ID data");
                Console.WriteLine($"\n❌ Failed to parse ID: {ex.Message}");
                Console.WriteLine($"Raw data: {rawBarcode}");
                return false;
            }
        }

        /// <summary>
        /// Test multiple images in a directory
        /// </summary>
        public async Task<int> TestDirectoryAsync(string directoryPath) {
            Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║   SYRIAN ID BARCODE DECODER - BATCH TEST MODE            ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            if (!Directory.Exists(directoryPath)) {
                Console.WriteLine($"❌ Directory not found: {directoryPath}");
                return 0;
            }

            var imageFiles = Directory.GetFiles(directoryPath, "*.jpg");
            Console.WriteLine($"📁 Found {imageFiles.Length} image(s) in directory");
            Console.WriteLine();

            int successCount = 0;
            int failCount = 0;

            for (int i = 0; i < imageFiles.Length; i++) {
                Console.WriteLine($"[{i + 1}/{imageFiles.Length}] Testing: {Path.GetFileName(imageFiles[i])}");
                Console.WriteLine();

                bool success = await TestImageAsync(imageFiles[i]);

                if (success) {
                    successCount++;
                } else {
                    failCount++;
                }

                if (i < imageFiles.Length - 1) {
                    Console.WriteLine();
                    Console.WriteLine("Press ENTER to continue to next image...");
                    Console.ReadLine();
                    Console.Clear();
                }
            }

            Console.WriteLine();
            Console.WriteLine("═══════════════════════════════════════════════════════════");
            Console.WriteLine("BATCH TEST SUMMARY");
            Console.WriteLine("═══════════════════════════════════════════════════════════");
            Console.WriteLine($"Total images:     {imageFiles.Length}");
            Console.WriteLine($"✓ Successful:     {successCount}");
            Console.WriteLine($"❌ Failed:         {failCount}");
            if (imageFiles.Length > 0) {
                Console.WriteLine($"Success rate:     {(successCount * 100.0 / imageFiles.Length):F1}%");
            }
            Console.WriteLine("═══════════════════════════════════════════════════════════");

            return successCount;
        }
    }
}