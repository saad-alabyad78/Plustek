using System;
using System.IO;

namespace Plustek.Configuration {
    public class AppSettings {
        // Paths
        public string OutputDirectory { get; }
        public string TempScanDirectory { get; }

        // Scanner
        public string WebSocketUrl { get; }
        public int ScanResolution { get; }
        public string PaperSize { get; }

        // Barcode
        public string BarcodeType { get; }
        public int BarcodePageNumber { get; }

        // Timeouts
        public int ScanTimeout { get; }
        public int FileWaitDelay { get; }

        public AppSettings() {
            OutputDirectory = Path.Combine(Directory.GetCurrentDirectory(), "scans");
            TempScanDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Temp", "WebFXScan"
            );

            WebSocketUrl = "ws://127.0.0.1:17778/webscan2";
            ScanResolution = 600;
            PaperSize = "2592x1944";

            BarcodeType = "pdf417";
            BarcodePageNumber = 103;

            ScanTimeout = 30000;
            FileWaitDelay = 3000;

            Directory.CreateDirectory(OutputDirectory);
        }

        public string GenerateOutputPath() {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            return Path.Combine(OutputDirectory, $"scan_{timestamp}.jpg");
        }

        public string GenerateOutputPathByNationalId(string nationalId) {
            // Create folder by National ID
            string idFolder = Path.Combine(OutputDirectory, nationalId);
            Directory.CreateDirectory(idFolder);

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            return Path.Combine(idFolder, $"scan_{timestamp}.jpg");
        }
    }
}