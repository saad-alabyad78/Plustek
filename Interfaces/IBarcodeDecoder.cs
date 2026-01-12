// File: Plustek/Interfaces/IBarcodeDecoder.cs
using BarcodeIdScan;
using Plustek.Models;
using System.Threading.Tasks;

namespace Plustek.Interfaces {
    /// <summary>
    /// Interface for barcode decoding operations
    /// </summary>
    public interface IBarcodeDecoder {
        /// <summary>
        /// Reads barcode from an image file
        /// </summary>
        /// <param name="imagePath">Path to the image file</param>
        /// <returns>Barcode read result, or null if no barcode found</returns>
        Task<BarcodeResult?> ReadAsync(string imagePath);

        /// <summary>
        /// Reads barcode with image enhancement techniques
        /// </summary>
        /// <param name="imagePath">Path to the image file (optional if last read path is cached)</param>
        /// <param name="enhancements">Array of enhancement techniques to apply (optional, uses default if null)</param>
        /// <returns>Barcode read result, or null if no barcode found</returns>
        Task<BarcodeResult?> ReadBarcodeWithEnhancementAsync(
            string? imagePath = null,
            EnhancementTechnique[]? enhancements = null
        );
    }
}