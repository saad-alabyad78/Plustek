using Plustek.Models;
using System.Threading.Tasks;

namespace Plustek.Interfaces {
    public interface IOutputWriter {
        Task SaveAsync(SyrianIdData data, string imagePath);
    }
}