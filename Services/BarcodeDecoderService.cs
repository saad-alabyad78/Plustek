using System;
using System.Text;
using System.Threading.Tasks;
using BarcodeIdScan;
using Plustek.Configuration;
using Plustek.Interfaces;
using Plustek.Models;

namespace Plustek.Services {
    public class BarcodeDecoderService : IBarcodeDecoder {
        private readonly AppSettings _settings;
        private readonly BarcodeIdScan.IBarcodeReader _sdkReader;

        public BarcodeDecoderService(AppSettings settings) {
            _settings = settings;
            _sdkReader = BarcodeReaderFactory.CreateReader(
                BarcodeReaderFactory.ReaderType.SDK
            );
        }

        public async Task<BarcodeResult?> ReadAsync(string imagePath) {
            try {
                var result = await Task.Run(() =>
                    _sdkReader.ReadBarcode(
                        imagePath,
                        _settings.BarcodeType,
                        _settings.BarcodePageNumber
                    )
                );

                if (!result.Success) {
                    return null;
                }

                // Decode base64 data manually
                string decodedText = DecodeBase64(result.DataBase64);

                if (string.IsNullOrEmpty(decodedText)) {
                    // Fallback to Text property if it exists
                    decodedText = result.Text ?? string.Empty;
                }

                if (string.IsNullOrEmpty(decodedText)) {
                    return null;
                }

                return new BarcodeResult {
                    Text = decodedText,
                    Type = result.Type ?? "PDF417",
                    Length = decodedText.Length
                };
            }
            catch (Exception ex) {
                Console.WriteLine($"Barcode read error: {ex.Message}");
                return null;
            }
        }

        private string DecodeBase64(string base64Data) {
            if (string.IsNullOrEmpty(base64Data)) {
                return string.Empty;
            }

            try {
                byte[] data = Convert.FromBase64String(base64Data);
                return Encoding.UTF8.GetString(data);
            }
            catch {
                return string.Empty;
            }
        }
    }
}