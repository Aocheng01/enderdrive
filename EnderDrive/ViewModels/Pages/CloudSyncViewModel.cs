using System;
using System.Collections.ObjectModel;
using System.IO;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnderDrive.Core.Cloud;
using EnderDrive.Core.Services;
using EnderDrive.Services;
using EnderDrive.Views.Dialogs;

namespace EnderDrive.ViewModels.Pages
{
    /// <summary>
    /// Página "Sincronización en la Nube": la conexión de la cuenta (en CloudSessionViewModel,
    /// compartido con el resto de la app) y la lista de copias que hay subidas.
    /// </summary>
    public partial class CloudSyncViewModel : ViewModelBase
    {
        private readonly ICloudProvider _provider;
        private readonly ISyncService _sync;
        private readonly IDialogService _dialogs;
        private readonly ToastViewModel _toast;
        private readonly ISettingsService _settings;
        private readonly IWorldScanner _scanner;

        public string Title => "Sincronización en la Nube";

        public CloudSessionViewModel Cloud { get; }

        /// <summary>Nombre del archivo que el desarrollador tiene que colocar junto al .exe.</summary>
        public string CredentialsFileName => App.GoogleCredentialsFileName;

        public ObservableCollection<CloudBackupItemViewModel> CloudBackups { get; } = [];

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasNoCloudBackups))]
        public partial bool IsLoadingBackups { get; set; }

        [ObservableProperty]
        public partial string CloudBackupsSummary { get; set; } = "";

        public bool HasNoCloudBackups => !IsLoadingBackups && CloudBackups.Count == 0;

        public CloudSyncViewModel(
            CloudSessionViewModel cloud,
            ICloudProvider provider,
            ISyncService sync,
            IDialogService dialogs,
            ToastViewModel toast,
            ISettingsService settings,
            IWorldScanner scanner)
        {
            Cloud = cloud;
            _provider = provider;
            _sync = sync;
            _dialogs = dialogs;
            _toast = toast;
            _settings = settings;
            _scanner = scanner;

            // Al conectar o desconectar la cuenta, recargamos (o vaciamos) la lista
            Cloud.PropertyChanged += OnCloudPropertyChanged;
        }

        /// <summary>Al entrar en la página, actualizamos el espacio usado y la lista.</summary>
        public override void OnNavigatedTo()
        {
            if (!Cloud.IsSignedIn)
                return;

            Cloud.RefreshCommand.Execute(null);
            _ = LoadBackupsAsync();
        }

        [RelayCommand]
        private async Task LoadBackupsAsync()
        {
            if (!Cloud.IsSignedIn)
            {
                CloudBackups.Clear();
                UpdateSummary();
                return;
            }

            IsLoadingBackups = true;
            try
            {
                var backups = await _provider.ListBackupsAsync();
                CloudBackups.Clear();
                foreach (var backup in backups)
                    CloudBackups.Add(new CloudBackupItemViewModel(backup));
            }
            catch (CloudException e)
            {
                Cloud.ErrorMessage = $"No se pudo leer la lista de copias: {e.Message}";
            }
            finally
            {
                IsLoadingBackups = false;
                UpdateSummary();
            }
        }

        /// <summary>Baja una copia concreta de la nube (también versiones antiguas) e instala el mundo.</summary>
        [RelayCommand]
        private async Task RestoreCloudBackupAsync(CloudBackupItemViewModel? item)
        {
            if (item is null)
                return;

            var savesPath = _settings.Current.SavesPath ?? _scanner.DefaultSavesPath;
            var confirmed = await _dialogs.ConfirmAsync(new ConfirmOptions(
                Title: "¿Restaurar esta copia de la nube?",
                Message: $"Se descargará la copia de «{item.WorldName}» subida {item.DateText.ToLowerInvariant()} "
                    + $"y se instalará en {Formatters.ShortPath(savesPath)}.\n\n"
                    + "Si el mundo ya existe, antes se guardará una copia de su estado actual en Copias de Seguridad.",
                ConfirmText: "Restaurar",
                Icon: "cloud_download"));
            if (!confirmed)
                return;

            var progress = _toast.Start($"Descargando de {Cloud.ProviderName}", item.WorldName);
            try
            {
                await _sync.DownloadWorldAsync(item.Backup, savesPath, progress);
                _toast.Succeed("Copia restaurada");
            }
            catch (Exception e) when (e is CloudException or IOException or InvalidDataException or UnauthorizedAccessException)
            {
                _toast.Fail(e.Message);
            }
        }

        private void OnCloudPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CloudSessionViewModel.Account))
                _ = LoadBackupsAsync();
        }

        private void UpdateSummary()
        {
            var worlds = CloudBackups.Select(b => b.WorldName).Distinct().Count();
            var size = Formatters.Size(CloudBackups.Sum(b => b.Backup.SizeBytes));
            CloudBackupsSummary = CloudBackups.Count == 0
                ? ""
                : $"{CloudBackups.Count} copias de {worlds} {(worlds == 1 ? "mundo" : "mundos")} · {size}";
            OnPropertyChanged(nameof(HasNoCloudBackups));
        }
    }

    /// <summary>Una copia de la nube preparada para la lista.</summary>
    public sealed class CloudBackupItemViewModel(CloudBackup backup)
    {
        public CloudBackup Backup { get; } = backup;
        public string WorldName => Backup.WorldFolderName;
        public string FileName => Backup.FileName;
        public string DateText { get; } = Formatters.RelativeDate(backup.CreatedAt);
        public string SizeText { get; } = Formatters.Size(backup.SizeBytes);
    }
}
