using System;

namespace Plustek.Models {
    public class SyrianIdData {
        public string? NationalId { get; set; }
        public string? FullNameArabic { get; set; }
        public string? FullNameEnglish { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? Gender { get; set; }
        public string? AddressArabic { get; set; }
        public DateTime? IssueDate { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public string? RawBarcodeData { get; set; }
    }
}