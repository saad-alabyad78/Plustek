using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Plustek.Models;
using ZXing;
using ZXing.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;

namespace Plustek.Services {
    [SupportedOSPlatform("windows")]
    public class OpenCvBarcodeDetector : IBarcodeDetector {
        private readonly ILogger<OpenCvBarcodeDetector> _logger;
        private readonly DecodingOptions _options;

        public OpenCvBarcodeDetector(ILogger<OpenCvBarcodeDetector> logger) {
            _logger = logger;

            _options = new DecodingOptions {
                TryHarder = true,
                TryInverted = true,
                PossibleFormats = new List<BarcodeFormat>
                {
                    BarcodeFormat.PDF_417,
                    BarcodeFormat.CODE_39,
                    BarcodeFormat.CODE_128,
                    BarcodeFormat.QR_CODE,
                    BarcodeFormat.DATA_MATRIX,
                    BarcodeFormat.AZTEC
                }
            };
        }

        public Task<string?> DetectAndDecodeAsync(Mat sourceImage) {
            if (sourceImage == null) throw new ArgumentNullException(nameof(sourceImage));
            if (sourceImage.Empty()) return Task.FromResult<string?>(null);

            try {
                // Step 1: candidate regions
                var candidates = FindBarcodeCandidates(sourceImage);
                _logger.LogInformation("Found {Count} candidate region(s).", candidates.Count);

                foreach (var rect in candidates) {
                    using var roi = new Mat(sourceImage, rect);
                    var decodedResult = TryDecodeMat(roi);

                    if (decodedResult != null && !string.IsNullOrEmpty(decodedResult.Text)) {
                        _logger.LogInformation("Decoded from candidate region.");
                        return Task.FromResult<string?>(decodedResult.Text);
                    }

                    // rotated variations (90°, 180°, 270°)
                    for (int angle = 90; angle <= 270; angle += 90) {
                        using var rotated = roi.Clone();
                        Cv2.Rotate(rotated, rotated, angle == 90 ? RotateFlags.Rotate90Clockwise :
                                                        angle == 180 ? RotateFlags.Rotate180 :
                                                        RotateFlags.Rotate90Counterclockwise);

                        var rotatedResult = TryDecodeMat(rotated);
                        if (rotatedResult != null && !string.IsNullOrEmpty(rotatedResult.Text)) {
                            _logger.LogInformation("Decoded from rotated candidate region ({Angle}).", angle);
                            return Task.FromResult<string?>(rotatedResult.Text);
                        }
                    }
                }

                // Step 2: fallback decode full image
                _logger.LogInformation("Attempting fallback: decode full image.");
                var fallback = TryDecodeMat(sourceImage);
                if (fallback != null && !string.IsNullOrEmpty(fallback.Text)) {
                    _logger.LogInformation("Decoded from full image fallback.");
                    return Task.FromResult<string?>(fallback.Text);
                }

                // Step 3: fallback inverted image
                using (var inv = new Mat()) {
                    Cv2.BitwiseNot(sourceImage, inv);
                    var invResult = TryDecodeMat(inv);
                    if (invResult != null && !string.IsNullOrEmpty(invResult.Text)) {
                        _logger.LogInformation("Decoded from inverted full image.");
                        return Task.FromResult<string?>(invResult.Text);
                    }
                }

                return Task.FromResult<string?>(null);
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Exception during barcode detection");
                return Task.FromResult<string?>(null);
            }
        }

        private BarcodeResult? TryDecodeMat(Mat mat) {
            try {
                using System.Drawing.Bitmap bitmap = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(mat);

                if (bitmap == null)
                    return null;

                var width = bitmap.Width;
                var height = bitmap.Height;
                var data = bitmap.LockBits(
                    new System.Drawing.Rectangle(0, 0, width, height),
                    System.Drawing.Imaging.ImageLockMode.ReadOnly,
                    System.Drawing.Imaging.PixelFormat.Format24bppRgb
                );

                var bytes = new byte[data.Stride * height];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                bitmap.UnlockBits(data);

                var luminanceSource = new RGBLuminanceSource(bytes, width, height);
                var binarizer = new HybridBinarizer(luminanceSource);
                var binaryBitmap = new BinaryBitmap(binarizer);

                var reader = new MultiFormatReader();
                reader.Hints = new Dictionary<DecodeHintType, object> {
                    { DecodeHintType.TRY_HARDER, true },
                    { DecodeHintType.POSSIBLE_FORMATS, _options.PossibleFormats }
                };

                var result = reader.decode(binaryBitmap);
                if (result == null)
                    return null;

                // Return raw bytes as ISO-8859-1 string (no encoding conversion)
                // The parser will handle proper encoding conversion
                string decodedText;
                if (result.RawBytes != null && result.RawBytes.Length > 0) {
                    // Use ISO-8859-1 (Latin-1) which preserves byte values 1:1
                    decodedText = System.Text.Encoding.GetEncoding("ISO-8859-1").GetString(result.RawBytes);
                } else {
                    // Fallback to ZXing's decoded text
                    decodedText = result.Text;
                }

                _logger.LogInformation("Barcode decoded successfully. Raw bytes length: {Length}", result.RawBytes?.Length ?? 0);

                return new BarcodeResult {
                    Text = decodedText,
                    Format = result.BarcodeFormat,
                    Points = result.ResultPoints,
                    Confidence = EstimateConfidence(result)
                };
            }
            catch (Exception ex) {
                _logger.LogDebug(ex, "ZXing decode failed");
                return null;
            }
        }

        private static int EstimateConfidence(ZXing.Result result) {
            int score = 50;

            if (!string.IsNullOrWhiteSpace(result.Text))
                score += 20;

            if (result.ResultPoints != null && result.ResultPoints.Length >= 4)
                score += 20;

            if (result.BarcodeFormat == ZXing.BarcodeFormat.PDF_417)
                score += 10; // IDs usually use PDF417

            return Math.Min(score, 100);
        }

        private List<Rect> FindBarcodeCandidates(Mat src) {
            var candidates = new List<Rect>();
            using var gray = new Mat();
            Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

            Cv2.GaussianBlur(gray, gray, new OpenCvSharp.Size(5, 5), 0);

            using var gradX = new Mat();
            Cv2.Sobel(gray, gradX, MatType.CV_32F, 1, 0, ksize: 3);

            using var absGradX = new Mat();
            Cv2.ConvertScaleAbs(gradX, absGradX);

            using var blurred = new Mat();
            Cv2.GaussianBlur(gray, blurred, new OpenCvSharp.Size(9, 9), 0);

            using var thresh = new Mat();
            Cv2.Threshold(blurred, thresh, 0, 255, ThresholdTypes.Otsu | ThresholdTypes.Binary);

            var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(21, 7));
            Cv2.MorphologyEx(thresh, thresh, MorphTypes.Close, kernel);

            Cv2.Erode(thresh, thresh, null, iterations: 2);
            Cv2.Dilate(thresh, thresh, null, iterations: 2);

            Cv2.FindContours(
                thresh,
                out OpenCvSharp.Point[][] contours,
                out HierarchyIndex[] hierarchy,
                RetrievalModes.External,
                ContourApproximationModes.ApproxSimple
            );

            var imageArea = src.Width * src.Height;
            foreach (var c in contours) {
                var rect = Cv2.BoundingRect(c);
                if (rect.Width < 20 || rect.Height < 20) continue;
                var area = rect.Width * rect.Height;
                var areaRatio = (double)area / imageArea;
                if (areaRatio < 0.0005) continue;

                double aspect = (double)rect.Width / rect.Height;
                if (aspect < 0.3 || aspect > 1.2) continue;

                int pad = (int)(Math.Min(rect.Width, rect.Height) * 0.15);
                var x = Math.Max(0, rect.X - pad);
                var y = Math.Max(0, rect.Y - pad);
                var w = Math.Min(src.Width - x, rect.Width + 2 * pad);
                var h = Math.Min(src.Height - y, rect.Height + 2 * pad);
                candidates.Add(new Rect(x, y, w, h));
            }

            return candidates.OrderByDescending(r => r.Width * r.Height).Take(6).ToList();
        }
    }
}