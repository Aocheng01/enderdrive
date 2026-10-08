using System.Threading.Tasks;
using Avalonia.Platform.Storage;


namespace EnderDrive.Services
{
    public sealed class FolderPicker : IFolderPicker
    {
        public async Task<string?> PickFolderAsync(string title)
        {
            // El diálogo necesita una ventana "dueña": usamos la ventana principal
            if (MainWindowLocator.Get() is not { } window)
                return null;

            var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
            });

            return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        }
    }
}
