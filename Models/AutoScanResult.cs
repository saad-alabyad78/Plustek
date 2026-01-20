using System;

namespace Plustek.Models {
    public class AutoScanResult {
        public string FileName { get; set; } = "";
        public string? Base64 { get; set; }
        public string? OcrText { get; set; }
        public DateTime ScannedAt { get; set; } = DateTime.Now;
    }
}