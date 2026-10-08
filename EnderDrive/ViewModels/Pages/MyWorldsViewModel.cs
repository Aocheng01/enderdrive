using System;
using System.Collections.ObjectModel;
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

    public string Title => "Mis Mundos Locales";

    public ObservableCollection<WorldItemViewModel> Worlds { get; } = [];

    [ObservableProperty]
    public partial string SavesPath { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoading { get; set; }

    public bool IsEmpty => !IsLoading && Worlds.Count == 0;

    public MyWorldsViewModel(IWorldScanner scanner, IFolderPicker folderPicker)
    {
        _scanner = scanner;
        _folderPicker = folderPicker;

        SavesPath = _scanner.DefaultSavesPath;
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
        await RefreshAsync();
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
}