using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;

namespace Plustek.Models {
    /// <summary>
    /// Represents parsed data from a Syrian National ID barcode
    /// </summary>
    public class SyrianIdData {
        public string? NationalId { get; set; }
        public string? FullNameArabic { get; set; }
        public string? FullNameEnglish { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? Gender { get; set; }
        public string? AddressArabic { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public DateTime? IssueDate { get; set; }
        public string? IssuingAuthority { get; set; }
        public string? DocumentNumber { get; set; }
        public string? RawData { get; set; }
        public string? RawDataOriginal { get; set; }
        public List<string> AllFields { get; set; } = new List<string>();
        public Dictionary<string, object> EncodedFields { get; set; } = new Dictionary<string, object>();

        public override string ToString() {
            var sb = new StringBuilder();
            sb.AppendLine("=== Syrian National ID ===");
            sb.AppendLine($"National ID: {NationalId ?? "N/A"}");
            sb.AppendLine($"Document Number: {DocumentNumber ?? "N/A"}");
            sb.AppendLine($"Name (Arabic): {FullNameArabic ?? "N/A"}");
            sb.AppendLine($"Name (English): {FullNameEnglish ?? "N/A"}");
            sb.AppendLine($"Date of Birth: {DateOfBirth?.ToString("dd-MM-yyyy") ?? "N/A"}");
            sb.AppendLine($"Gender: {Gender ?? "N/A"}");
            sb.AppendLine($"Address: {AddressArabic ?? "N/A"}");
            sb.AppendLine($"Issue Date: {IssueDate?.ToString("dd-MM-yyyy") ?? "N/A"}");
            sb.AppendLine($"Expiry Date: {ExpiryDate?.ToString("dd-MM-yyyy") ?? "N/A"}");
            sb.AppendLine($"Issuing Authority: {IssuingAuthority ?? "N/A"}");

            sb.AppendLine("\n=== All Detected Fields ===");
            for (int i = 0; i < AllFields.Count; i++) {
                sb.AppendLine($"Field {i}: {AllFields[i]}");
            }

            if (EncodedFields.Count > 0) {
                sb.AppendLine("\n=== Decoded Binary Fields ===");
                foreach (var kvp in EncodedFields) {
                    sb.AppendLine($"{kvp.Key}: {kvp.Value}");
                }
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Parses Syrian National ID PDF417 barcode data with binary field decoding
    /// </summary>
    public static class SyrianIdParser {
        public static SyrianIdData Parse(string rawBarcodeData) {
            if (string.IsNullOrEmpty(rawBarcodeData)) {
                throw new ArgumentException("Barcode data cannot be null or empty");
            }

            var result = new SyrianIdData {
                RawDataOriginal = rawBarcodeData
            };

            try {
                // Split the original data first
                string[] originalFields = rawBarcodeData.Split('#');

                // Convert each field individually
                List<string> processedFields = new List<string>();
                foreach (var field in originalFields) {
                    string convertedField = ConvertFieldToArabic(field);
                    processedFields.Add(convertedField);
                }

                result.AllFields = processedFields;
                result.RawData = string.Join("#", processedFields);
                string[] fields = processedFields.ToArray();

                // Parse human-readable fields (0-5)
                ParseReadableFields(result, fields);

                // Decode binary/encoded fields (6+)
                DecodeBinaryFields(result, originalFields);

            }
            catch (Exception ex) {
                Console.WriteLine($"Error parsing ID data: {ex.Message}");
            }

            return result;
        }

        private static void ParseReadableFields(SyrianIdData result, string[] fields) {
            // Field 0: Document type
            if (fields.Length > 0) {
                result.DocumentNumber = CleanField(fields[0]);
            }

            // Field 1: First name Arabic
            if (fields.Length > 1) {
                result.FullNameArabic = CleanField(fields[1]);
            }

            // Field 2: Family name or additional name
            if (fields.Length > 2) {
                result.FullNameEnglish = CleanField(fields[2]);
            }

            // Field 3: Address Arabic
            if (fields.Length > 3) {
                result.AddressArabic = CleanField(fields[3]);
            }

            // Field 4: Place and date of birth
            if (fields.Length > 4) {
                result.DateOfBirth = ExtractDate(fields[4]);
                // Extract place name
                var placeMatch = System.Text.RegularExpressions.Regex.Match(
                    fields[4],
                    @"^(.+?)\s+\d"
                );
                if (placeMatch.Success) {
                    result.IssuingAuthority = placeMatch.Groups[1].Value.Trim();
                }
            }

            // Field 5: National ID
            if (fields.Length > 5) {
                result.NationalId = CleanField(fields[5]);
                if (!string.IsNullOrEmpty(result.NationalId)) {
                    result.Gender = DetermineGender(result.NationalId);
                }
            }
        }

        private static void DecodeBinaryFields(SyrianIdData result, string[] originalFields) {
            // Fields 6+ contain encoded data
            for (int i = 6; i < originalFields.Length; i++) {
                var field = originalFields[i];

                // Try different decoding methods
                var decoded = new Dictionary<string, object>();

                // 1. Check for dates in various formats
                var dates = ExtractAllDates(field);
                if (dates.Count > 0) {
                    decoded["Dates"] = string.Join(", ", dates.Select(d => d.ToString("dd-MM-yyyy")));
                }

                // 2. Extract numeric sequences (could be reference numbers, codes)
                var numbers = ExtractNumericSequences(field);
                if (numbers.Count > 0) {
                    decoded["NumericCodes"] = string.Join(", ", numbers);
                }

                // 3. Check for Base64 encoded data
                var base64Data = TryDecodeBase64(field);
                if (base64Data != null) {
                    decoded["Base64Data"] = base64Data;
                }

                // 4. Analyze byte patterns
                var byteAnalysis = AnalyzeBytePattern(field);
                if (byteAnalysis.Count > 0) {
                    foreach (var kvp in byteAnalysis) {
                        decoded[kvp.Key] = kvp.Value;
                    }
                }

                // 5. Check for hexadecimal patterns
                var hexData = ExtractHexPatterns(field);
                if (hexData.Count > 0) {
                    decoded["HexPatterns"] = string.Join(", ", hexData);
                }

                if (decoded.Count > 0) {
                    result.EncodedFields[$"Field_{i}"] = decoded;
                }
            }

            // Try to extract issue/expiry dates from binary fields
            ExtractIssuanceData(result, originalFields);
        }

        private static List<DateTime> ExtractAllDates(string field) {
            var dates = new List<DateTime>();

            // Pattern 1: DD-MM-YYYY or DD/MM/YYYY
            var matches = System.Text.RegularExpressions.Regex.Matches(
                field,
                @"(\d{1,2})[-/](\d{1,2})[-/](\d{4})"
            );

            foreach (System.Text.RegularExpressions.Match match in matches) {
                try {
                    int day = int.Parse(match.Groups[1].Value);
                    int month = int.Parse(match.Groups[2].Value);
                    int year = int.Parse(match.Groups[3].Value);
                    if (year >= 1900 && year <= 2100 && month >= 1 && month <= 12 && day >= 1 && day <= 31) {
                        dates.Add(new DateTime(year, month, day));
                    }
                }
                catch { }
            }

            // Pattern 2: YYYYMMDD
            var matches2 = System.Text.RegularExpressions.Regex.Matches(field, @"(20\d{6}|19\d{6})");
            foreach (System.Text.RegularExpressions.Match match in matches2) {
                try {
                    string dateStr = match.Value;
                    int year = int.Parse(dateStr.Substring(0, 4));
                    int month = int.Parse(dateStr.Substring(4, 2));
                    int day = int.Parse(dateStr.Substring(6, 2));
                    if (month >= 1 && month <= 12 && day >= 1 && day <= 31) {
                        dates.Add(new DateTime(year, month, day));
                    }
                }
                catch { }
            }

            return dates;
        }

        private static List<string> ExtractNumericSequences(string field) {
            var sequences = new List<string>();
            var matches = System.Text.RegularExpressions.Regex.Matches(field, @"\d{4,}");

            foreach (System.Text.RegularExpressions.Match match in matches) {
                sequences.Add(match.Value);
            }

            return sequences;
        }

        private static string? TryDecodeBase64(string field) {
            try {
                // Check if field looks like Base64
                if (System.Text.RegularExpressions.Regex.IsMatch(field, @"^[A-Za-z0-9+/=]+$")) {
                    byte[] bytes = Convert.FromBase64String(field);
                    string decoded = Encoding.UTF8.GetString(bytes);
                    if (!string.IsNullOrWhiteSpace(decoded)) {
                        return decoded;
                    }
                }
            }
            catch { }
            return null;
        }

        private static Dictionary<string, object> AnalyzeBytePattern(string field) {
            var analysis = new Dictionary<string, object>();

            try {
                byte[] bytes = Encoding.GetEncoding("ISO-8859-1").GetBytes(field);

                // Check for common patterns
                analysis["Length"] = bytes.Length;

                // Check if it might be a hash (MD5=16 bytes, SHA1=20 bytes, SHA256=32 bytes)
                if (bytes.Length == 16) analysis["PossibleType"] = "MD5 Hash";
                else if (bytes.Length == 20) analysis["PossibleType"] = "SHA1 Hash";
                else if (bytes.Length == 32) analysis["PossibleType"] = "SHA256 Hash";
                else if (bytes.Length > 100) analysis["PossibleType"] = "Biometric/Signature Data";

                // Calculate entropy (randomness)
                double entropy = CalculateEntropy(bytes);
                analysis["Entropy"] = Math.Round(entropy, 2);

                if (entropy > 7.5) {
                    analysis["Likely"] = "Encrypted/Random Data";
                } else if (entropy > 6.0) {
                    analysis["Likely"] = "Compressed/Encoded Data";
                }

            }
            catch { }

            return analysis;
        }

        private static double CalculateEntropy(byte[] bytes) {
            var frequency = new int[256];
            foreach (byte b in bytes) {
                frequency[b]++;
            }

            double entropy = 0;
            int length = bytes.Length;
            foreach (int freq in frequency) {
                if (freq > 0) {
                    double probability = (double)freq / length;
                    entropy -= probability * Math.Log(probability, 2);
                }
            }

            return entropy;
        }

        private static List<string> ExtractHexPatterns(string field) {
            var hexPatterns = new List<string>();

            // Look for patterns like "3859>?>4408?=8"
            var matches = System.Text.RegularExpressions.Regex.Matches(
                field,
                @"[\da-fA-F]{4,}"
            );

            foreach (System.Text.RegularExpressions.Match match in matches) {
                hexPatterns.Add(match.Value);
            }

            return hexPatterns;
        }

        private static void ExtractIssuanceData(SyrianIdData result, string[] fields) {
            // The last field often contains issue/expiry information
            if (fields.Length > 12) {
                var lastField = fields[fields.Length - 1];

                // Look for date patterns
                var dates = ExtractAllDates(lastField);
                if (dates.Count >= 2) {
                    // Usually: issue date, expiry date
                    result.IssueDate = dates[0];
                    result.ExpiryDate = dates[1];
                } else if (dates.Count == 1) {
                    // Might be expiry date
                    result.ExpiryDate = dates[0];
                }

                // Look for numeric pattern at the end (often contains encoded dates)
                var match = System.Text.RegularExpressions.Regex.Match(
                    lastField,
                    @"(\d{4})[>?=]+(\d{4})"
                );
                if (match.Success && result.IssueDate == null) {
                    try {
                        // Try to decode as YYMM format
                        string issueCode = match.Groups[1].Value;
                        string expiryCode = match.Groups[2].Value;

                        // This is speculation - actual format would need Syrian ID documentation
                        result.EncodedFields["IssueDateCode"] = issueCode;
                        result.EncodedFields["ExpiryDateCode"] = expiryCode;
                    }
                    catch { }
                }
            }
        }

        private static string ConvertFieldToArabic(string field) {
            if (string.IsNullOrEmpty(field)) return field;

            try {
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                byte[] bytes = System.Text.Encoding.GetEncoding("ISO-8859-1").GetBytes(field);
                string converted = System.Text.Encoding.GetEncoding("Windows-1256").GetString(bytes);

                if (IsValidArabicText(converted)) {
                    return converted;
                }

                return field;
            }
            catch {
                return field;
            }
        }

        private static bool IsValidArabicText(string text) {
            if (string.IsNullOrEmpty(text)) return false;

            int arabicChars = 0;
            int totalChars = 0;
            int spaces = 0;
            int digits = 0;
            int commonPunctuation = 0;

            foreach (char c in text) {
                totalChars++;

                if (c >= 0x0600 && c <= 0x06FF) {
                    arabicChars++;
                } else if (char.IsWhiteSpace(c)) {
                    spaces++;
                } else if (char.IsDigit(c)) {
                    digits++;
                } else if (c == '-' || c == '/' || c == ',' || c == '.') {
                    commonPunctuation++;
                }
            }

            int validChars = arabicChars + spaces + digits + commonPunctuation;
            double validRatio = (double)validChars / totalChars;
            double arabicRatio = (double)arabicChars / totalChars;

            return arabicChars > 0 && validRatio > 0.7 && arabicRatio > 0.3;
        }

        private static string DetermineGender(string nationalId) {
            if (string.IsNullOrEmpty(nationalId) || nationalId.Length < 2) {
                return "Unknown";
            }

            try {
                if (nationalId.Length == 11) {
                    int lastDigit = int.Parse(nationalId[10].ToString());
                    if (lastDigit % 2 == 1) return "Male";
                    if (lastDigit % 2 == 0) return "Female";
                }

                if (nationalId.Length == 14) {
                    int genderDigit = int.Parse(nationalId[12].ToString());
                    return genderDigit % 2 == 1 ? "Male" : "Female";
                }

                if (nationalId.Length > 0) {
                    int lastDigit = int.Parse(nationalId[nationalId.Length - 1].ToString());
                    return lastDigit % 2 == 1 ? "Male" : "Female";
                }
            }
            catch { }

            return "Unknown";
        }

        private static string CleanField(string field) {
            if (string.IsNullOrWhiteSpace(field)) return string.Empty;

            field = field.Trim();
            field = System.Text.RegularExpressions.Regex.Replace(field, @"\s+", " ");

            return field;
        }

        private static DateTime? ExtractDate(string field) {
            if (string.IsNullOrWhiteSpace(field)) return null;

            var match = System.Text.RegularExpressions.Regex.Match(
                field,
                @"(\d{1,2})[-/](\d{1,2})[-/](\d{4})"
            );

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
    }
}