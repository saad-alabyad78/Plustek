using System.Threading.Tasks;
using Plustek.Models;

namespace Plustek.Interfaces {
    public interface IBarcodeDecoder {
        Task<BarcodeResult?> ReadAsync(string imagePath);
    }
}