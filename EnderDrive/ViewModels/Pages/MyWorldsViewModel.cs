using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnderDrive.Core.Models;
using EnderDrive.Core.Services;
using EnderDrive.Services;

namespace EnderDrive.ViewModels.Pages;

public partial class MyWorldsViewModel : ViewModelBase
{
    private const string AllTypes = "Todos los tipos";
    private const string HardcoreType = "Hardcore";
    private const string SortRecent = "Recientes";
    private const string SortName = "Nombre";
    private const string SortSize = "Tamaño";

    private readonly IWorldScanner _scanner;
    private readonly IFolderPicker _folderPicker;
    private readonly IFolderLauncher _folderLauncher;
    private readonly IClipboardService _clipboard;
    private readonly ISettingsService _settings;

    // Todos los mundos encontrados. "Worlds" es lo que se ve tras buscar/filtrar/ordenar.
    private List<WorldItemViewModel> _allWorlds = [];
    private string? _scanError;

    public string Title => "Mis Mundos Locales";

    public ObservableCollection<WorldItemViewModel> Worlds { get; } = [];

    public IReadOnlyList<string> TypeFilters { get; } =
        [AllTypes, "Supervivencia", "Creativo", "Aventura", "Espectador", HardcoreType];

    public IReadOnlyList<string> SortOptions { get; } = [SortRecent, SortName, SortSize];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomPath), nameof(SavesPathShort))]
    public partial string SavesPath { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; }

    [ObservableProperty]
    public partial string SelectedTypeFilter { get; set; }

    [ObservableProperty]
    public partial string SelectedSort { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial WorldItemViewModel? SelectedWorld { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoading { get; set; }

    // ===== Barra de resumen =====
    [ObservableProperty]
    public partial string StatusText { get; set; }

    [ObservableProperty]
    public partial string TotalSizeText { get; set; }

    [ObservableProperty]
    public partial string LastScanText { get; set; }

    // Datos de prueba para la cuota de la nube (Fase 3: datos reales de Drive, igual que el menú lateral)
    public string CloudUsageText => "34.2 GB";
    public string CloudQuotaText => "/ 50 GB (68%)";
    public double CloudPercent => 68.4;
    public string CloudLevelText => "68";

    public bool IsEmpty => !IsLoading && Worlds.Count == 0;
    public bool HasSelection => SelectedWorld is not null;

    /// <summary>true si el usuario eligió una carpeta distinta de la de por defecto.</summary>
    public bool IsCustomPath => !string.Equals(SavesPath, _scanner.DefaultSavesPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>".minecraft/saves" o las dos últimas carpetas de la ruta elegida.</summary>
    public string SavesPathShort
    {
        get
        {
            var parts = SavesPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar,
                StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 ? $"{parts[^2]}/{parts[^1]}" : SavesPath;
        }
    }

    public MyWorldsViewModel(
        IWorldScanner scanner,
        IFolderPicker folderPicker,
        IFolderLauncher folderLauncher,
        IClipboardService clipboard,
        ISettingsService settings)
    {
        _scanner = scanner;
        _folderPicker = folderPicker;
        _folderLauncher = folderLauncher;
        _clipboard = clipboard;
        _settings = settings;

        // Si el usuario eligió una carpeta en otra sesión, la recuperamos
        SavesPath = _settings.Current.SavesPath ?? _scanner.DefaultSavesPath;
        SearchText = "";
        SelectedTypeFilter = AllTypes;
        SelectedSort = SortRecent;
        StatusText = "";
        TotalSizeText = "";
        LastScanText = "";

        // Primer escaneo al arrancar (el constructor no puede ser async)
        _ = RefreshAsync();
    }

    // Cada vez que cambia la búsqueda, el filtro o el orden, recalculamos la lista visible
    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedTypeFilterChanged(string value) => ApplyFilter();
    partial void OnSelectedSortChanged(string value) => ApplyFilter();

    [RelayCommand]
    private async Task ChooseFolderAsync()
    {
        var path = await _folderPicker.PickFolderAsync("Elige la carpeta saves de Minecraft");
        if (path is null)
            return; // el usuario canceló

        SavesPath = path;
        SaveSavesPath(path);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ResetFolderAsync()
    {
        SavesPath = _scanner.DefaultSavesPath;
        SaveSavesPath(null);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var previousSelection = SelectedWorld?.FolderPath;

        IsLoading = true;
        _scanError = null;
        _allWorlds = [];
        ApplyFilter();

        try
        {
            var worlds = await _scanner.ScanAsync(SavesPath);
            _allWorlds = worlds.Select(w => new WorldItemViewModel(w, _folderLauncher, _clipboard)).ToList();
        }
        catch (Exception e)
        {
            _scanError = $"No se pudo leer la carpeta: {e.Message}";
        }
        finally
        {
            IsLoading = false;
            LastScanText = $"Último escaneo: {DateTime.Now:HH:mm}";
            ApplyFilter();

            // Mantenemos el mundo que estaba seleccionado; si no, el primero de la lista
            SelectedWorld = Worlds.FirstOrDefault(w => w.FolderPath == previousSelection)
                ?? Worlds.FirstOrDefault();
        }
    }

    private void ApplyFilter()
    {
        IEnumerable<WorldItemViewModel> query = _allWorlds;

        if (!string.IsNullOrWhiteSpace(SearchText))
            query = query.Where(w => w.Matches(SearchText.Trim()));

        query = SelectedTypeFilter switch
        {
            HardcoreType => query.Where(w => w.IsHardcore),
            "Supervivencia" => query.Where(w => w.GameMode == GameMode.Survival && !w.IsHardcore),
            "Creativo" => query.Where(w => w.GameMode == GameMode.Creative),
            "Aventura" => query.Where(w => w.GameMode == GameMode.Adventure),
            "Espectador" => query.Where(w => w.GameMode == GameMode.Spectator),
            _ => query,
        };

        query = SelectedSort switch
        {
            SortName => query.OrderBy(w => w.Name, StringComparer.CurrentCultureIgnoreCase),
            SortSize => query.OrderByDescending(w => w.SizeBytes),
            _ => query.OrderByDescending(w => w.LastPlayed),
        };

        var selected = SelectedWorld;

        Worlds.Clear();
        foreach (var world in query)
            Worlds.Add(world);

        // Clear() quita la selección del ListBox: la recuperamos si el mundo sigue visible
        SelectedWorld = selected is not null && Worlds.Contains(selected) ? selected : Worlds.FirstOrDefault();

        UpdateSummary();
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void UpdateSummary()
    {
        TotalSizeText = $"{WorldItemViewModel.FormatSize(_allWorlds.Sum(w => w.SizeBytes))} ocupados";

        StatusText = IsLoading ? "Buscando mundos…"
            : _scanError ?? (Worlds.Count == _allWorlds.Count
                ? $"{_allWorlds.Count} Mundos detectados"
                : $"{Worlds.Count} de {_allWorlds.Count} Mundos");
    }

    private void SaveSavesPath(string? path)
    {
        _settings.Current.SavesPath = path;

        try
        {
            _settings.Save();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // No es crítico: la app sigue funcionando, solo no recordará la carpeta
        }
    }
}
