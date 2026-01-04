using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Plustek.Services;
using Plustek.Models;

namespace Plustek {
    public class Runner {
        private readonly IImageLoader _imageLoader;
        private readonly IBarcodeDetector _detector;
        private readonly IOutputWriter _outputWriter;
        private readonly ILogger<Runner> _logger;

        public Runner(IImageLoader imageLoader, IBarcodeDetector detector, IOutputWriter outputWriter, ILogger<Runner> logger) {
            _imageLoader = imageLoader;
            _detector = detector;
            _outputWriter = outputWriter;
            _logger = logger;
        }

        public async Task<int> RunAsync(string imagePath) {
            var mat = await _imageLoader.LoadImageAsync(imagePath);
            if (mat == null || mat.Empty()) {
                _logger.LogError("Failed to load image or image empty.");
                return 2;
            }

            var rawBarcode = await _detector.DetectAndDecodeAsync(mat);
            if (string.IsNullOrEmpty(rawBarcode)) {
                _logger.LogWarning("No barcode could be decoded from the image.");
                Console.WriteLine("No barcode detected/decoded.");
                return 1;
            }

            _logger.LogInformation("Raw barcode data length: {Length} characters", rawBarcode.Length);

            // Parse the Egyptian ID data (barcode is already correctly decoded!)
            try {
                var idData = SyrianIdParser.Parse(rawBarcode);

                // Display to console
                Console.WriteLine("\n" + idData.ToString());

                // Save to HTML and TXT files
                await _outputWriter.SaveToFileAsync(idData, imagePath);

            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to parse ID data");
                Console.WriteLine($"\nFailed to parse ID: {ex.Message}");
                Console.WriteLine($"Raw data: {rawBarcode}");
                return 1;
            }

            return 0;
        }
    }
}