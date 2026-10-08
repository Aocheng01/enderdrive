using System.Threading.Tasks;

namespace EnderDrive.Services
{
    public interface IClipboardService
    {
        Task SetTextAsync(string text);
    }
}
