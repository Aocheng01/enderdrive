using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnderDrive.Core.Services;
using EnderDrive.Services;

namespace EnderDrive.ViewModels.Pages
{
    public partial class SettingsViewModel : ViewModelBase
    {
        private readonly ISettingsService _settings;
        private readonly IBackupService _backupService;
        private readonly IFolderPicker _folderPicker;
        private readonly IFolderLauncher _folderLauncher;

        // Evita guardar mientras cargamos los valores en OnNavigatedTo
        private bool _isLoading;

        public string Title => "Ajustes";

        public string BackupsPath => _backupService.BackupsPath;
        public bool IsCustomBackupsPath => _settings.Current.BackupsPath is not null;

        [ObservableProperty]
        public partial bool AutoUploadOnWorldClose { get; set; }

        [ObservableProperty]
        public partial bool NotifyCloudChangesOnStartup { get; set; }

        /// <summary>decimal? porque es el tipo que usa NumericUpDown.</summary>
        [ObservableProperty]
        public partial decimal? MaxBackupsPerWorld { get; set; }

        public SettingsViewModel(
            ISettingsService settings,
            IBackupService backupService,
            IFolderPicker folderPicker,
            IFolderLauncher folderLauncher)
        {
            _settings = settings;
            _backupService = backupService;
            _folderPicker = folderPicker;
            _folderLauncher = folderLauncher;
            LoadValues();
        }

        public override void OnNavigatedTo() => LoadValues();

        partial void OnAutoUploadOnWorldCloseChanged(bool value)
        {
            if (_isLoading)
                return;
            _settings.Current.AutoUploadOnWorldClose = value;
            Save();
        }

        partial void OnNotifyCloudChangesOnStartupChanged(bool value)
        {
            if (_isLoading)
                return;
            _settings.Current.NotifyCloudChangesOnStartup = value;
            Save();
        }

        partial void OnMaxBackupsPerWorldChanged(decimal? value)
        {
            if (_isLoading || value is null)
                return;

            _settings.Current.MaxBackupsPerWorld = (int)Math.Clamp(value.Value, 1, 100);
            Save();
        }

        [RelayCommand]
        private async Task ChooseBackupsFolderAsync()
        {
            var path = await _folderPicker.PickFolderAsync("Elige dónde guardar las copias de seguridad");
            if (path is null)
                return;

            _settings.Current.BackupsPath = path;
            Save();
            LoadValues();
        }

        [RelayCommand]
        private void ResetBackupsFolder()
        {
            _settings.Current.BackupsPath = null;
            Save();
            LoadValues();
        }

        [RelayCommand]
        private Task OpenBackupsFolderAsync()
        {
            Directory.CreateDirectory(BackupsPath);
            return _folderLauncher.OpenAsync(BackupsPath);
        }

        private void LoadValues()
        {
            _isLoading = true;
            MaxBackupsPerWorld = _settings.Current.MaxBackupsPerWorld;
            AutoUploadOnWorldClose = _settings.Current.AutoUploadOnWorldClose;
            NotifyCloudChangesOnStartup = _settings.Current.NotifyCloudChangesOnStartup;
            _isLoading = false;

            OnPropertyChanged(nameof(BackupsPath));
            OnPropertyChanged(nameof(IsCustomBackupsPath));
        }

        private void Save()
        {
            try
            {
                _settings.Save();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // No es crítico: el cambio se aplica en esta sesión aunque no se guarde en disco
            }
        }
    }
}
