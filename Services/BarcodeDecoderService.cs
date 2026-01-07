using BarcodeIdScan;
using Plustek.Configuration;
using Plustek.Interfaces;
using Plustek.Models;
using System;
using System.Text;
using System.Threading.Tasks;

namespace Plustek.Services {
    public class BarcodeDecoderService : IBarcodeDecoder {
        private readonly AppSettings _settings;
        private readonly BarcodeIdScan.IBarcodeReader _sdkReader;

        public BarcodeDecoderService(AppSettings settings) {
            _settings = settings;
            _sdkReader = BarcodeReaderFactory.CreateReader(
                BarcodeReaderFactory.ReaderType.CLI
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

                Console.WriteLine($"[DEBUG] Base64 length: {result.DataBase64?.Length ?? 0}");

                // Decode base64 with ISO-8859-1 encoding (for Arabic text)
                string decodedText = DecodeBase64ToISO(result.DataBase64 ?? string.Empty);

                Console.WriteLine($"[DEBUG] Decoded text length: {decodedText.Length}");
                Console.WriteLine($"[DEBUG] First 100 chars: {decodedText.Substring(0, Math.Min(100, decodedText.Length))}");

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

        private string DecodeBase64ToISO(string base64Data) {
            if (string.IsNullOrEmpty(base64Data)) {
                return string.Empty;
            }

            try {
                // Decode base64 to bytes
                byte[] data = Convert.FromBase64String(base64Data);

                // IMPORTANT: Use ISO-8859-1 encoding (Latin-1)
                // This preserves the raw byte values which will be converted to Windows-1256 (Arabic) later
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding("ISO-8859-1").GetString(data);
            }
            catch (Exception ex) {
                Console.WriteLine($"Base64 decode error: {ex.Message}");
                return string.Empty;
            }
        }
    }
}