using System.Threading.Tasks;
using OpenCvSharp;

namespace Plustek.Services {
    public interface IBarcodeDetector {
        /// <summary>
        /// Detects barcode region(s) and attempts to decode. Returns the decoded text or null if none.
        /// </summary>
        Task<string?> DetectAndDecodeAsync(Mat sourceImage);
    }
}
