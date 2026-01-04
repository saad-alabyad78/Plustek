using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Plustek.Services;

namespace Plustek.Services {
    public class OpenCvImageLoader : IImageLoader {
        private readonly ILogger<OpenCvImageLoader> _logger;

        public OpenCvImageLoader(ILogger<OpenCvImageLoader> logger) {
            _logger = logger;
        }

        public Task<Mat?> LoadImageAsync(string path) {
            if (string.IsNullOrWhiteSpace(path)) {
                _logger.LogError("Image path is empty.");
                return Task.FromResult<Mat?>(null);
            }

            if (!File.Exists(path)) {
                _logger.LogError("Image file does not exist: {Path}", path);
                return Task.FromResult<Mat?>(null);
            }

            try {
                var mat = Cv2.ImRead(path, ImreadModes.Color);
                if (mat.Empty()) {
                    _logger.LogError("Loaded Mat is empty for path: {Path}", path);
                    return Task.FromResult<Mat?>(null);
                }

                return Task.FromResult<Mat?>(mat);
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Exception while loading image {Path}", path);
                return Task.FromResult<Mat?>(null);
            }
        }
    }
}
