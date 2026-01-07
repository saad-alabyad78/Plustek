using System;
using System.Text;
using System.Linq;
using Plustek.Models;

namespace Plustek.Parsers {
    public static class SyrianIdParser {
        public static SyrianIdData? Parse(string barcodeData) {
            if (string.IsNullOrEmpty(barcodeData)) {
                return null;
            }

            var data = new SyrianIdData {
                RawBarcodeData = barcodeData
            };

            try {
                // Register encoding provider for Arabic
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

                // Split by # delimiter
                string[] rawFields = barcodeData.Split('#');

                Console.WriteLine($"[DEBUG] Total raw fields: {rawFields.Length}");

                // Convert each field from ISO-8859-1 to Windows-1256 (Arabic)
                foreach (var field in rawFields) {
                    string converted = ConvertToArabic(field);
                    data.Fields.Add(converted);

                    // Debug: show first 50 chars of each field
                    string preview = converted.Length > 50
                        ? converted.Substring(0, 50) + "..."
                        : converted;
                    Console.WriteLine($"[DEBUG] Field: {preview}");
                }

                Console.WriteLine($"[DEBUG] Successfully parsed {data.Fields.Count} fields");
            }
            catch (Exception ex) {
                Console.WriteLine($"[ERROR] Parser error: {ex.Message}");
                return null;
            }

            return data;
        }

        private static string ConvertToArabic(string text) {
            if (string.IsNullOrEmpty(text)) {
                return string.Empty;
            }

            try {
                // Convert from ISO-8859-1 (Latin-1) to Windows-1256 (Arabic)
                var isoEncoding = Encoding.GetEncoding("ISO-8859-1");
                var arabicEncoding = Encoding.GetEncoding("Windows-1256");

                byte[] isoBytes = isoEncoding.GetBytes(text);
                string arabicText = arabicEncoding.GetString(isoBytes);

                // Check if conversion produced valid Arabic text
                if (IsValidArabicText(arabicText)) {
                    return arabicText;
                }

                // If not valid Arabic, return original
                return text;
            }
            catch (Exception ex) {
                Console.WriteLine($"[WARN] Encoding conversion failed: {ex.Message}");
                return text;
            }
        }

        private static bool IsValidArabicText(string text) {
            if (string.IsNullOrEmpty(text)) {
                return false;
            }

            // Count Arabic characters (Unicode range 0x0600-0x06FF)
            int arabicChars = text.Count(c => c >= 0x0600 && c <= 0x06FF);
            int totalChars = text.Length;
            int spaces = text.Count(char.IsWhiteSpace);
            int digits = text.Count(char.IsDigit);
            int punctuation = text.Count(c => c == '-' || c == '/' || c == ',' || c == '.');

            // Valid if contains Arabic characters and most chars are valid
            int validChars = arabicChars + spaces + digits + punctuation;
            double validRatio = (double)validChars / totalChars;

            return arabicChars > 0 && validRatio > 0.7;
        }
    }
}