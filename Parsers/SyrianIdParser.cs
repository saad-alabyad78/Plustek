using System;
using System.Text;
using System.Text.RegularExpressions;
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

            string[] fields = barcodeData.Split('#');

            // Convert fields to Arabic
            for (int i = 0; i < fields.Length; i++) {
                fields[i] = ConvertToArabic(fields[i]);
            }

            // Field 1: Name Arabic
            if (fields.Length > 1) {
                data.FullNameArabic = fields[1].Trim();
            }

            // Field 2: Name English
            if (fields.Length > 2) {
                data.FullNameEnglish = fields[2].Trim();
            }

            // Field 3: Address
            if (fields.Length > 3) {
                data.AddressArabic = fields[3].Trim();
            }

            // Field 4: Date of Birth
            if (fields.Length > 4) {
                data.DateOfBirth = ExtractDate(fields[4]);
            }

            // Field 5: National ID
            if (fields.Length > 5) {
                data.NationalId = fields[5].Trim();
                data.Gender = DetermineGender(data.NationalId);
            }

            return data;
        }

        private static string ConvertToArabic(string text) {
            try {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                var bytes = Encoding.GetEncoding("ISO-8859-1").GetBytes(text);
                return Encoding.GetEncoding("Windows-1256").GetString(bytes);
            }
            catch {
                return text;
            }
        }

        private static DateTime? ExtractDate(string text) {
            var match = Regex.Match(text, @"(\d{1,2})[-/](\d{1,2})[-/](\d{4})");
            if (match.Success) {
                try {
                    int day = int.Parse(match.Groups[1].Value);
                    int month = int.Parse(match.Groups[2].Value);
                    int year = int.Parse(match.Groups[3].Value);
                    return new DateTime(year, month, day);
                }
                catch { }
            }
            return null;
        }

        private static string DetermineGender(string nationalId) {
            if (string.IsNullOrEmpty(nationalId)) {
                return "Unknown";
            }

            try {
                int lastDigit = int.Parse(nationalId[^1].ToString());
                return lastDigit % 2 == 1 ? "Male" : "Female";
            }
            catch {
                return "Unknown";
            }
        }
    }
}