using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnderDrive.Core.Cloud;

namespace EnderDrive.ViewModels
{
    /// <summary>
    /// Estado de la cuenta en la nube, compartido por toda la app (singleton):
    /// el menú lateral, el avatar, la barra de Mis Mundos y la página de Sincronización
    /// leen de aquí, así que al conectar o desconectar se actualizan todos a la vez.
    /// </summary>
    public sealed partial class CloudSessionViewModel : ObservableObject
    {
        private readonly ICloudProvider _provider;
        private CancellationTokenSource? _signInCancellation;

        public string ProviderName => _provider.DisplayName;
        public bool IsConfigured => _provider.IsConfigured;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsSignedOut), nameof(AvatarText), nameof(SidebarQuotaText), nameof(SidebarSubtitle))]
        public partial CloudAccount? Account { get; set; }

        /// <summary>Esperando al navegador o recuperando la sesión.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsSignedOut))]
        public partial bool IsBusy { get; set; }

        [ObservableProperty]
        public partial bool IsWaitingForBrowser { get; set; }

        [ObservableProperty]
        public partial string? ErrorMessage { get; set; }

        /// <summary>Lo calcula Mis Mundos al comparar los mundos con la nube (null = sin calcular).</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SidebarSubtitle))]
        public partial int? SyncedWorldsCount { get; set; }

        public bool IsSignedIn => Account is not null;
        public bool IsSignedOut => Account is null && !IsBusy && IsConfigured;

        // ===== Textos listos para la vista =====
        public string DisplayName => Account?.DisplayName ?? "";
        public string Email => Account?.Email ?? "";

        /// <summary>Inicial del nombre para el avatar redondo.</summary>
        public string AvatarText => string.IsNullOrEmpty(Account?.DisplayName) ? "" : Account.DisplayName[..1].ToUpperInvariant();

        public string QuotaUsedText => Account is null ? "—" : Formatters.Size(Account.Quota.UsedBytes);

        public string QuotaLimitText => Account?.Quota.LimitBytes is { } limit
            ? $"/ {Formatters.Size(limit)} ({Account.Quota.Percent:0}%)"
            : Account is null ? "Sin conectar" : "sin límite";

        public double QuotaPercent => Account?.Quota.Percent ?? 0;
        public string QuotaLevelText => Account is null ? "–" : $"{QuotaPercent:0}";

        public string SidebarQuotaText => Account?.Quota.LimitBytes is { } limit
            ? $"{Formatters.Size(Account.Quota.UsedBytes)} / {Formatters.Size(limit)}"
            : Account is null ? "—" : Formatters.Size(Account.Quota.UsedBytes);

        public string SidebarSubtitle => Account is null ? $"{_provider.DisplayName} sin conectar"
            : SyncedWorldsCount is { } count ? (count == 1 ? "1 mundo sincronizado" : $"{count} mundos sincronizados")
            : Account.Email;

        public string AvatarTooltip => Account is null ? $"{_provider.DisplayName} sin conectar" : Account.Email;

        public CloudSessionViewModel(ICloudProvider provider)
        {
            _provider = provider;
        }

        /// <summary>Al arrancar: si ya se conectó otra vez, recupera la sesión sin abrir el navegador.</summary>
        public async Task RestoreAsync()
        {
            if (!IsConfigured)
                return;

            IsBusy = true;
            try
            {
                SetAccount(await _provider.RestoreSessionAsync());
            }
            catch (CloudException e)
            {
                ErrorMessage = $"No se pudo conectar con {ProviderName}: {e.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task SignInAsync()
        {
            ErrorMessage = null;
            IsBusy = true;
            IsWaitingForBrowser = true;
            _signInCancellation = new CancellationTokenSource();

            try
            {
                SetAccount(await _provider.SignInAsync(_signInCancellation.Token));
            }
            catch (OperationCanceledException)
            {
                // El usuario pulsó "Cancelar": no es un error
            }
            catch (CloudException e)
            {
                ErrorMessage = $"No se pudo iniciar sesión: {e.Message}";
            }
            finally
            {
                IsBusy = false;
                IsWaitingForBrowser = false;
                _signInCancellation.Dispose();
                _signInCancellation = null;
            }
        }

        /// <summary>Si el usuario cierra el navegador sin aceptar, la espera no terminaría nunca.</summary>
        [RelayCommand]
        private void CancelSignIn() => _signInCancellation?.Cancel();

        [RelayCommand]
        private async Task SignOutAsync()
        {
            IsBusy = true;
            try
            {
                await _provider.SignOutAsync();
            }
            finally
            {
                SetAccount(null);
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task RefreshAsync()
        {
            if (Account is null)
                return;

            try
            {
                SetAccount(await _provider.GetAccountAsync());
                ErrorMessage = null;
            }
            catch (CloudException e)
            {
                ErrorMessage = $"No se pudo actualizar: {e.Message}";
            }
        }

        private void SetAccount(CloudAccount? account)
        {
            Account = account;

            // Todas estas propiedades se calculan a partir de Account
            OnPropertyChanged(nameof(IsSignedIn));
            OnPropertyChanged(nameof(AvatarTooltip));
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(Email));
            OnPropertyChanged(nameof(QuotaUsedText));
            OnPropertyChanged(nameof(QuotaLimitText));
            OnPropertyChanged(nameof(QuotaPercent));
            OnPropertyChanged(nameof(QuotaLevelText));
        }
    }
}
