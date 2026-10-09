using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnderDrive.Core.Cloud;

namespace EnderDrive.ViewModels.Pages
{
    /// <summary>
    /// Página "Sincronización en la Nube": la conexión de la cuenta (en CloudSessionViewModel,
    /// compartido con el resto de la app) y la lista de copias que hay subidas.
    /// </summary>
    public partial class CloudSyncViewModel : ViewModelBase
    {
        private readonly ICloudProvider _provider;

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

        public CloudSyncViewModel(CloudSessionViewModel cloud, ICloudProvider provider)
        {
            Cloud = cloud;
            _provider = provider;

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
