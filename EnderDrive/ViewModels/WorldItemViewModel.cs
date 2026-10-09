using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnderDrive.Core.Models;
using EnderDrive.Core.Services;
using EnderDrive.Services;
using System;
using System.Globalization;
using System.Threading.Tasks;

namespace EnderDrive.ViewModels
{
    /// <summary>
    /// Un mundo preparado para mostrarse en una tarjeta.
    /// No hereda de ViewModelBase para que el ViewLocator no intente buscarle una vista:
    /// se dibuja con el DataTemplate de MyWorldsView.
    /// </summary>
    public sealed partial class WorldItemViewModel : ObservableObject
    {
        private readonly IFolderLauncher _folderLauncher;
        private readonly IClipboardService _clipboard;

        // Datos en bruto (para filtrar y ordenar)
        public string Name { get; }
        public string FolderName { get; }
        public string FolderPath { get; }
        public DateTime LastPlayed { get; }
        public long SizeBytes { get; }
        public GameMode? GameMode { get; }
        public bool IsHardcore { get; }
        public string? GameVersion { get; }
        public string? Loader { get; }

        // Textos listos para la vista
        public string LastPlayedText { get; }
        public string SizeText { get; }
        public string? SeedText { get; }
        public string? VersionBadgeText { get; }
        public string LoaderChipText { get; }
        public string? GameModeText { get; }
        public string SelectionText => $"{Name} ({SizeText})";

        // Color de la etiqueta del loader
        public bool IsModded { get; }
        public bool IsPluginServer { get; }

        public Bitmap? Icon { get; }
        public bool HasIcon => Icon is not null;

        // ===== Estado respecto a la nube =====

        /// <summary>null = no se sabe (sin cuenta conectada o todavía comprobando).</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsSynced), nameof(HasPendingChanges), nameof(ShowEnableButton), nameof(SyncBadgeText))]
        public partial SyncState? SyncState { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SyncBadgeText))]
        public partial bool IsCheckingSync { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SyncBadgeText))]
        public partial long PendingBytes { get; set; }

        [ObservableProperty]
        public partial string? LastUploadText { get; set; }

        public bool IsSynced => SyncState == Core.Services.SyncState.Synced;
        public bool HasPendingChanges => SyncState == Core.Services.SyncState.PendingChanges;

        /// <summary>"Habilitar Cloud Sync": cuando nunca se subió o no sabemos el estado.</summary>
        public bool ShowEnableButton => !IsSynced && !HasPendingChanges;

        public string SyncBadgeText => IsCheckingSync ? "Comprobando la nube…" : SyncState switch
        {
            Core.Services.SyncState.Synced => "Sincronizado con la nube",
            Core.Services.SyncState.PendingChanges => $"Cambios locales pendientes ({Formatters.Size(PendingBytes)})",
            Core.Services.SyncState.LocalOnly => "Solo en local",
            _ => "Nube sin conectar",
        };

        /// <summary>Aplica el resultado de comparar el mundo con la nube.</summary>
        public void ApplySyncStatus(WorldSyncStatus? status)
        {
            IsCheckingSync = false;
            SyncState = status?.State;
            PendingBytes = status?.PendingBytes ?? 0;
            LastUploadText = status?.LastUpload is { } last
                ? $"Última subida: {Formatters.RelativeDate(last.CreatedAt)}"
                : null;
        }

        public WorldItemViewModel(WorldInfo world, IFolderLauncher folderLauncher, IClipboardService clipboard)
        {
            _folderLauncher = folderLauncher;
            _clipboard = clipboard;

            Name = world.Name;
            FolderName = world.FolderName;
            FolderPath = world.FolderPath;
            LastPlayed = world.LastPlayed;
            SizeBytes = world.SizeBytes;
            GameMode = world.GameMode;
            IsHardcore = world.IsHardcore;
            GameVersion = world.GameVersion;
            Loader = FormatLoader(world.Loader);

            LastPlayedText = Formatters.RelativeDate(world.LastPlayed);
            SizeText = Formatters.Size(world.SizeBytes);
            SeedText = world.Seed?.ToString(CultureInfo.InvariantCulture);
            VersionBadgeText = world.GameVersion is null ? null : $"v{world.GameVersion}";
            GameModeText = FormatGameMode(world.GameMode);

            // "Hardcore 1.20.4", "Fabric 1.21", "Vanilla 26.3"…
            var prefix = IsHardcore ? "Hardcore" : Loader ?? "Vanilla";
            LoaderChipText = world.GameVersion is null ? prefix : $"{prefix} {world.GameVersion}";

            var brand = world.Loader?.ToLowerInvariant();
            IsModded = brand is "fabric" or "forge" or "neoforge" or "quilt";
            IsPluginServer = brand is "paper" or "spigot" or "purpur" or "bukkit";

            Icon = LoadIcon(world.IconPath);
        }

        /// <summary>true si el mundo coincide con el texto del buscador.</summary>
        public bool Matches(string query)
            => Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || FolderName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || (SeedText?.Contains(query, StringComparison.Ordinal) ?? false)
            || LoaderChipText.Contains(query, StringComparison.CurrentCultureIgnoreCase);

        [RelayCommand]
        private Task OpenFolderAsync() => _folderLauncher.OpenAsync(FolderPath);

        [RelayCommand]
        private Task CopySeedAsync() => SeedText is null ? Task.CompletedTask : _clipboard.SetTextAsync(SeedText);

        [RelayCommand]
        private Task CopyPathAsync() => _clipboard.SetTextAsync(FolderPath);

        private static string? FormatLoader(string? brand) => brand?.ToLowerInvariant() switch
        {
            null or "" => null,
            "vanilla" => "Vanilla",
            "fabric" => "Fabric",
            "forge" => "Forge",
            "neoforge" => "NeoForge",
            "quilt" => "Quilt",
            "paper" => "Paper",
            _ => char.ToUpperInvariant(brand[0]) + brand[1..],
        };

        private static string? FormatGameMode(GameMode? mode) => mode switch
        {
            Core.Models.GameMode.Survival => "Supervivencia",
            Core.Models.GameMode.Creative => "Creativo",
            Core.Models.GameMode.Adventure => "Aventura",
            Core.Models.GameMode.Spectator => "Espectador",
            _ => null,
        };

        private static Bitmap? LoadIcon(string? path)
        {
            if (path is null)
                return null;

            try
            {
                return new Bitmap(path);
            }
            catch (Exception)
            {
                return null; // icon.png dañado: se mostrará el icono por defecto
            }
        }
    }
}
