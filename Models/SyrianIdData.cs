// File: Plustek/Models/SyrianIdData.cs
using System.Collections.Generic;

namespace Plustek.Models {
    public class SyrianIdData {
        public string? RawBarcodeData { get; set; }
        public List<string> Fields { get; set; } = new List<string>();

        // Convenience properties for easy access to parsed fields
        // Based on Syrian ID barcode format (14 fields total)

        /// <summary>
        /// Field 0: First Name (الاسم الأول)
        /// </summary>
        public string FirstName => Fields.Count > 0 ? Fields[0].Trim() : "";

        /// <summary>
        /// Field 1: Last Name / Family Name (الكنية)
        /// </summary>
        public string LastName => Fields.Count > 1 ? Fields[1].Trim() : "";

        /// <summary>
        /// Field 2: Father's Name (اسم الأب)
        /// </summary>
        public string FatherName => Fields.Count > 2 ? Fields[2].Trim() : "";

        /// <summary>
        /// Field 3: Mother's Full Name (اسم الأم الكامل)
        /// </summary>
        public string MotherName => Fields.Count > 3 ? Fields[3].Trim() : "";

        /// <summary>
        /// Field 4: Place and Date of Birth (مكان وتاريخ الولادة)
        /// Format: "City DD-MM-YYYY" e.g., "دمشق 20-7-2003"
        /// </summary>
        public string BirthInfo => Fields.Count > 4 ? Fields[4].Trim() : "";

        /// <summary>
        /// Field 5: National ID Number (الرقم الوطني) - 11 digits
        /// This is the primary key identifier
        /// </summary>
        public string NationalId => Fields.Count > 5 ? Fields[5].Trim() : "";

        /// <summary>
        /// Field 6: Unknown/Reserved field (possibly registration or issuance info)
        /// </summary>
        public string Field6 => Fields.Count > 6 ? Fields[6].Trim() : "";

        /// <summary>
        /// Field 7: Unknown/Reserved field
        /// </summary>
        public string Field7 => Fields.Count > 7 ? Fields[7].Trim() : "";

        /// <summary>
        /// Field 8: Unknown/Reserved field
        /// </summary>
        public string Field8 => Fields.Count > 8 ? Fields[8].Trim() : "";

        /// <summary>
        /// Field 9: Unknown/Reserved field (possibly encoded data)
        /// </summary>
        public string Field9 => Fields.Count > 9 ? Fields[9].Trim() : "";

        /// <summary>
        /// Field 10: Unknown/Reserved field (possibly encoded data)
        /// </summary>
        public string Field10 => Fields.Count > 10 ? Fields[10].Trim() : "";

        /// <summary>
        /// Field 11: Unknown/Reserved field
        /// </summary>
        public string Field11 => Fields.Count > 11 ? Fields[11].Trim() : "";

        /// <summary>
        /// Field 12: Unknown/Reserved field (possibly security or verification data)
        /// </summary>
        public string Field12 => Fields.Count > 12 ? Fields[12].Trim() : "";

        /// <summary>
        /// Field 13: Control/Checksum field
        /// Format: Appears to be a checksum or control sequence
        /// </summary>
        public string ChecksumField => Fields.Count > 13 ? Fields[13].Trim() : "";

        // Parsed birth information (extracted from Field 4)

        /// <summary>
        /// Birth City extracted from BirthInfo (field 4)
        /// Example: "دمشق" from "دمشق 20-7-2003"
        /// </summary>
        public string BirthCity {
            get {
                if (string.IsNullOrEmpty(BirthInfo)) return "";
                var parts = BirthInfo.Split(' ');
                return parts.Length > 0 ? parts[0].Trim() : "";
            }
        }

        /// <summary>
        /// Birth Date extracted from BirthInfo (field 4)
        /// Example: "20-7-2003" from "دمشق 20-7-2003"
        /// </summary>
        public string BirthDate {
            get {
                if (string.IsNullOrEmpty(BirthInfo)) return "";
                var parts = BirthInfo.Split(' ');
                return parts.Length > 1 ? parts[1].Trim() : "";
            }
        }

        /// <summary>
        /// Full Name in Arabic (combines First Name, Father Name, Last Name)
        /// Format: "الاسم الأول اسم الأب الكنية"
        /// </summary>
        public string FullNameArabic => $"{FirstName} {FatherName} {LastName}".Trim();

        /// <summary>
        /// Checks if the essential fields are populated
        /// </summary>
        public bool IsValid =>
            !string.IsNullOrEmpty(NationalId) &&
            NationalId.Length == 11 &&
            !string.IsNullOrEmpty(FirstName);

        /// <summary>
        /// Total number of fields parsed from barcode
        /// </summary>
        public int FieldCount => Fields.Count;
    }
}