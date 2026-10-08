using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnderDrive.Core.Services;
using EnderDrive.Services;

namespace EnderDrive.ViewModels.Pages;

public partial class MyWorldsViewModel : ViewModelBase
{
    private readonly IWorldScanner _scanner;
    private readonly IFolderPicker _folderPicker;
    private readonly IFolderLauncher _folderLauncher;
    private readonly ISettingsService _settings;

    public string Title => "Mis Mundos Locales";

    public ObservableCollection<WorldItemViewModel> Worlds { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomPath))]
    public partial string SavesPath { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoading { get; set; }

    public bool IsEmpty => !IsLoading && Worlds.Count == 0;

    /// <summary>true si el usuario eligió una carpeta distinta de la de por defecto.</summary>
    public bool IsCustomPath => !string.Equals(SavesPath, _scanner.DefaultSavesPath, StringComparison.OrdinalIgnoreCase);

    public MyWorldsViewModel(
        IWorldScanner scanner,
        IFolderPicker folderPicker,
        IFolderLauncher folderLauncher,
        ISettingsService settings)
    {
        _scanner = scanner;
        _folderPicker = folderPicker;
        _folderLauncher = folderLauncher;
        _settings = settings;

        // Si el usuario eligió una carpeta en otra sesión, la recuperamos
        SavesPath = _settings.Current.SavesPath ?? _scanner.DefaultSavesPath;
        StatusText = "";

        // Primer escaneo al arrancar (el constructor no puede ser async)
        _ = RefreshAsync();
    }

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
    private async Task OpenFolderAsync(WorldItemViewModel? world)
    {
        if (world is not null)
            await _folderLauncher.OpenAsync(world.FolderPath);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        StatusText = "Buscando mundos…";
        Worlds.Clear();

        try
        {
            var worlds = await _scanner.ScanAsync(SavesPath);
            foreach (var world in worlds)
                Worlds.Add(new WorldItemViewModel(world));

            StatusText = $"{Worlds.Count} mundos encontrados";
        }
        catch (Exception e)
        {
            StatusText = $"No se pudo leer la carpeta: {e.Message}";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
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
