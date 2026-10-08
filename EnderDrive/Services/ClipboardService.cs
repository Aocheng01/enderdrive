using System.Threading.Tasks;
using Avalonia.Input.Platform;

namespace EnderDrive.Services
{
    public sealed class ClipboardService : IClipboardService
    {
        public async Task SetTextAsync(string text)
        {
            // El portapapeles pertenece a la ventana (TopLevel), igual que los diálogos
            if (MainWindowLocator.Get()?.Clipboard is { } clipboard)
                await clipboard.SetTextAsync(text);
        }
    }
}
