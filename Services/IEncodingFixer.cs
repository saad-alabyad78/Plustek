using System;
using System.Text;

namespace Plustek.Services {
    public interface IEncodingFixer {
        string FixArabicEncoding(string text);
    }

    public class EncodingFixerService : IEncodingFixer {
        private readonly Encoding _sourceEncoding;
        private readonly Encoding _targetEncoding;

        public EncodingFixerService() {
            // Register code pages for Windows-1256 support
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            _sourceEncoding = Encoding.GetEncoding("ISO-8859-1");
            _targetEncoding = Encoding.GetEncoding("Windows-1256");
        }

        public string FixArabicEncoding(string text) {
            if (string.IsNullOrEmpty(text)) {
                return text;
            }

            try {
                // Debug: Show what we're converting
                Console.WriteLine($"\n[DEBUG] Original text sample: {text.Substring(0, Math.Min(50, text.Length))}");

                // Convert string to bytes using the encoding ZXing used (ISO-8859-1)
                byte[] bytes = _sourceEncoding.GetBytes(text);

                Console.WriteLine($"[DEBUG] Byte array length: {bytes.Length}");
                Console.WriteLine($"[DEBUG] First 20 bytes: {string.Join(" ", bytes.Take(20).Select(b => b.ToString("X2")))}");

                // Re-interpret those bytes as Windows-1256 (Arabic)
                string arabicText = _targetEncoding.GetString(bytes);

                Console.WriteLine($"[DEBUG] Converted text sample: {arabicText.Substring(0, Math.Min(50, arabicText.Length))}");

                return arabicText;
            }
            catch (Exception ex) {
                Console.WriteLine($"[ERROR] Failed to fix encoding: {ex.Message}");
                return text;
            }
        }
    }
}