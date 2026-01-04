using System.Threading.Tasks;
using OpenCvSharp;

namespace Plustek.Services {
    public interface IImageLoader {
        Task<Mat?> LoadImageAsync(string path);
    }
}
