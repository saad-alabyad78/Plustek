using System.Threading.Tasks;
using Plustek.Models;

namespace Plustek.Interfaces {
    public interface IOutputWriter {
        Task SaveAsync(SyrianIdData data, string imagePath);
    }
}