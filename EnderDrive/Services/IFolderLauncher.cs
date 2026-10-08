using System.Threading.Tasks;

namespace EnderDrive.Services
{
    public interface IFolderLauncher
    {
        /// <summary>Abre la carpeta en el explorador de archivos del sistema.</summary>
        Task OpenAsync(string folderPath);
    }
}
