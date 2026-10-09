using System.IO;
using EnderDrive.Core.Models;

namespace EnderDrive.ViewModels
{
    /// <summary>Una copia de seguridad preparada para mostrarse en la lista.</summary>
    public sealed class BackupItemViewModel
    {
        public BackupInfo Info { get; }

        public string WorldName => Info.WorldFolderName;
        public string FileName { get; }
        public string DateText { get; }
        public string FullDateText { get; }
        public string SizeText { get; }

        public bool IsAutomatic => Info.Reason == BackupReason.BeforeRestore;
        public string ReasonText => IsAutomatic ? "Antes de restaurar" : "Manual";
        public string Icon => IsAutomatic ? "settings_backup_restore" : "folder_zip";

        public string SelectionText => $"{WorldName} · {FullDateText}";

        public BackupItemViewModel(BackupInfo info)
        {
            Info = info;
            FileName = Path.GetFileName(info.FilePath);
            DateText = Formatters.RelativeDate(info.CreatedAt);
            FullDateText = Formatters.FullDate(info.CreatedAt);
            SizeText = Formatters.Size(info.SizeBytes);
        }
    }
}
