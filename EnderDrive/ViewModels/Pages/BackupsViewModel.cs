using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnderDrive.Core.Services;
using EnderDrive.Services;
using EnderDrive.Views.Dialogs;

namespace EnderDrive.ViewModels.Pages
{
    public partial class BackupsViewModel : ViewModelBase
    {
        private const string AllWorlds = "Todos los mundos";

        private readonly IBackupService _backupService;
        private readonly IWorldScanner _scanner;
        private readonly ISettingsService _settings;
        private readonly IDialogService _dialogs;
        private readonly IFolderLauncher _folderLauncher;
        private readonly ToastViewModel _toast;

        private List<BackupItemViewModel> _all = [];

        // Mundo que hay que mostrar al terminar de cargar (lo pide Mis Mundos con ShowWorld)
        private string? _pendingWorldFilter;

        public string Title => "Copias de Seguridad";

        public ObservableCollection<BackupItemViewModel> Backups { get; } = [];

        public ObservableCollection<string> WorldFilters { get; } = [AllWorlds];

        [ObservableProperty]
        public partial string? SelectedWorldFilter { get; set; } = AllWorlds;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSelection))]
        public partial BackupItemViewModel? SelectedBackup { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsEmpty))]
        public partial bool IsLoading { get; set; }

        [ObservableProperty]
        public partial string StatusText { get; set; } = "";

        [ObservableProperty]
        public partial string TotalSizeText { get; set; } = "";

        public string BackupsPath => _backupService.BackupsPath;
        public string BackupsPathShort => Formatters.ShortPath(BackupsPath);
        public string RetentionText => $"Se guardan las últimas {_settings.Current.MaxBackupsPerWorld} copias de cada mundo";

        public bool IsEmpty => !IsLoading && Backups.Count == 0;
        public bool HasSelection => SelectedBackup is not null;

        private string SavesPath => _settings.Current.SavesPath ?? _scanner.DefaultSavesPath;

        public BackupsViewModel(
            IBackupService backupService,
            IWorldScanner scanner,
            ISettingsService settings,
            IDialogService dialogs,
            IFolderLauncher folderLauncher,
            ToastViewModel toast)
        {
            _backupService = backupService;
            _scanner = scanner;
            _settings = settings;
            _dialogs = dialogs;
            _folderLauncher = folderLauncher;
            _toast = toast;
        }

        /// <summary>Al entrar en la página, recargamos: puede haber copias nuevas o ajustes cambiados.</summary>
        public override void OnNavigatedTo() => _ = RefreshAsync();

        /// <summary>Filtra la lista por un mundo cuando se termine de cargar.</summary>
        public void ShowWorld(string worldFolderName) => _pendingWorldFilter = worldFolderName;

        partial void OnSelectedWorldFilterChanged(string? value) => ApplyFilter();

        [RelayCommand]
        private async Task RefreshAsync()
        {
            IsLoading = true;
            OnPropertyChanged(nameof(BackupsPath));
            OnPropertyChanged(nameof(BackupsPathShort));
            OnPropertyChanged(nameof(RetentionText));

            try
            {
                var backups = await _backupService.GetBackupsAsync();
                _all = backups.Select(b => new BackupItemViewModel(b)).ToList();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _all = [];
                _toast.Fail($"No se pudo leer la carpeta de copias: {e.Message}");
            }
            finally
            {
                IsLoading = false;
            }

            // Rehacemos la lista de mundos del desplegable, conservando la selección
            var filter = _pendingWorldFilter ?? SelectedWorldFilter ?? AllWorlds;
            _pendingWorldFilter = null;

            WorldFilters.Clear();
            WorldFilters.Add(AllWorlds);
            foreach (var world in _all.Select(b => b.WorldName).Distinct().Order(StringComparer.CurrentCultureIgnoreCase))
                WorldFilters.Add(world);

            if (!WorldFilters.Contains(filter))
                filter = AllWorlds;

            SelectedWorldFilter = filter;
            ApplyFilter();
        }

        [RelayCommand]
        private Task OpenBackupsFolderAsync()
        {
            Directory.CreateDirectory(BackupsPath);
            return _folderLauncher.OpenAsync(BackupsPath);
        }

        [RelayCommand]
        private Task RevealSelectedAsync()
            => SelectedBackup is { } backup
                ? _folderLauncher.OpenAsync(Path.GetDirectoryName(backup.Info.FilePath)!)
                : Task.CompletedTask;

        [RelayCommand]
        private async Task RestoreSelectedAsync()
        {
            if (SelectedBackup is not { } backup)
                return;

            var destination = $"{Formatters.ShortPath(SavesPath)}/{backup.WorldName}";
            var confirmed = await _dialogs.ConfirmAsync(new ConfirmOptions(
                Title: "¿Restaurar esta copia?",
                Message: $"El mundo «{backup.WorldName}» volverá a como estaba el {backup.FullDateText}.\n\n"
                    + "Antes se guardará automáticamente una copia del estado actual, así que podrás deshacerlo.\n\n"
                    + $"Destino: {destination}",
                ConfirmText: "Restaurar",
                Icon: "history"));

            if (!confirmed)
                return;

            var progress = _toast.Start("Restaurando copia", backup.WorldName);
            try
            {
                await _backupService.RestoreAsync(backup.Info, SavesPath, progress);
                _toast.Succeed("Copia restaurada");
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                _toast.Fail(e.Message);
            }

            await RefreshAsync();
        }

        [RelayCommand]
        private async Task DeleteSelectedAsync()
        {
            if (SelectedBackup is not { } backup)
                return;

            var confirmed = await _dialogs.ConfirmAsync(new ConfirmOptions(
                Title: "¿Eliminar esta copia?",
                Message: $"Se borrará la copia de «{backup.WorldName}» del {backup.FullDateText} ({backup.SizeText}). "
                    + "Esta acción no se puede deshacer.",
                ConfirmText: "Eliminar",
                Icon: "delete",
                IsDestructive: true));

            if (!confirmed)
                return;

            try
            {
                _backupService.Delete(backup.Info);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _toast.Fail($"No se pudo eliminar: {e.Message}");
            }

            await RefreshAsync();
        }

        private void ApplyFilter()
        {
            var selectedPath = SelectedBackup?.Info.FilePath;

            var visible = SelectedWorldFilter is null or AllWorlds
                ? _all
                : _all.Where(b => b.WorldName == SelectedWorldFilter);

            Backups.Clear();
            foreach (var backup in visible)
                Backups.Add(backup);

            SelectedBackup = Backups.FirstOrDefault(b => b.Info.FilePath == selectedPath) ?? Backups.FirstOrDefault();

            var count = Backups.Count;
            StatusText = count == 1 ? "1 copia" : $"{count} copias";
            TotalSizeText = $"{Formatters.Size(Backups.Sum(b => b.Info.SizeBytes))} ocupados";
            OnPropertyChanged(nameof(IsEmpty));
        }
    }
}
