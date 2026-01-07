using Plustek.Models;
using System.Threading.Tasks;

namespace Plustek.Interfaces {
    public interface IScanner {
        Task<bool> ConnectAsync();
        Task<bool> InitializeAsync();
        Task<ScanResult?> ScanAsync(string outputPath);
        Task DisconnectAsync();
    }
}