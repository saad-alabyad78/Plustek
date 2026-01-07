namespace Plustek.Models {
    public class BarcodeResult {
        public string Text { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int Length { get; set; }
    }
}