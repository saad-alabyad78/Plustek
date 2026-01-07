using Plustek.Models;
using System.Threading.Tasks;

namespace Plustek.Interfaces {
    public interface IBarcodeDecoder {
        Task<BarcodeResult?> ReadAsync(string imagePath);
    }
}