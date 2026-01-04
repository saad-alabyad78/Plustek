using ZXing;

namespace Plustek.Models {
    public class BarcodeResult {
        public string Text { get; init; } = string.Empty;
        public BarcodeFormat Format { get; init; }
        public ResultPoint[]? Points { get; init; }
        public int Confidence { get; init; }   // heuristic score (0–100)
    }
}
