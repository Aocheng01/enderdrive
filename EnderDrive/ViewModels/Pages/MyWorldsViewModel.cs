using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnderDrive.Core.Cloud;
using EnderDrive.Core.Models;
using EnderDrive.Core.Services;
using EnderDrive.Services;
using EnderDrive.Views.Dialogs;

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
    private readonly IBackupService _backupService;
    private readonly INavigationService _navigation;
    private readonly BackupsViewModel _backupsPage;
    private readonly ToastViewModel _toast;
    private readonly CloudSessionViewModel _cloud;
    private readonly ISyncService _sync;
    private readonly CloudSyncViewModel _cloudPage;
    private readonly ICloudProvider _cloudProvider;
    private readonly IDialogService _dialogs;
    private readonly WorldActivityMonitor _activity;

    // El aviso "hay novedades en la nube" solo se muestra una vez, al arrancar
    private bool _startupNoticeDone;

    // Si se pide comprobar la nube dos veces seguidas, solo vale el resultado de la última
    private int _syncCheckVersion;

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
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(HasLocalSelection))]
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

    /// <summary>"3 de 5 sincronizados" (vacío si no hay cuenta conectada).</summary>
    [ObservableProperty]
    public partial string SyncSummaryText { get; set; } = "";

    /// <summary>Cuenta en la nube (para la barra de cuota del resumen).</summary>
    public CloudSessionViewModel Cloud => _cloud;

    public bool IsEmpty => !IsLoading && Worlds.Count == 0;
    public bool HasSelection => SelectedWorld is not null;

    /// <summary>Hay un mundo seleccionado y existe en este PC (no es "Solo en la nube").</summary>
    public bool HasLocalSelection => SelectedWorld is { IsCloudOnly: false };

    /// <summary>true si el usuario eligió una carpeta distinta de la de por defecto.</summary>
    public bool IsCustomPath => !string.Equals(SavesPath, _scanner.DefaultSavesPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>".minecraft/saves" o las dos últimas carpetas de la ruta elegida.</summary>
    public string SavesPathShort => Formatters.ShortPath(SavesPath);

    public MyWorldsViewModel(
        IWorldScanner scanner,
        IFolderPicker folderPicker,
        IFolderLauncher folderLauncher,
        IClipboardService clipboard,
        ISettingsService settings,
        IBackupService backupService,
        INavigationService navigation,
        BackupsViewModel backupsPage,
        ToastViewModel toast,
        CloudSessionViewModel cloud,
        ISyncService sync,
        CloudSyncViewModel cloudPage,
        ICloudProvider cloudProvider,
        IDialogService dialogs,
        WorldActivityMonitor activity)
    {
        _scanner = scanner;
        _folderPicker = folderPicker;
        _folderLauncher = folderLauncher;
        _clipboard = clipboard;
        _settings = settings;
        _backupService = backupService;
        _navigation = navigation;
        _backupsPage = backupsPage;
        _toast = toast;
        _cloud = cloud;
        _sync = sync;
        _cloudPage = cloudPage;
        _cloudProvider = cloudProvider;
        _dialogs = dialogs;
        _activity = activity;

        // El vigilante avisa desde un hilo en segundo plano: pasamos al hilo de la interfaz
        _activity.WorldOpened += path => Dispatcher.UIThread.Post(() => OnWorldOpened(path));
        _activity.WorldClosed += path => Dispatcher.UIThread.Post(() => _ = OnWorldClosedAsync(path));

        // Al conectar o desconectar la cuenta, volvemos a comprobar el estado de los mundos
        _cloud.PropertyChanged += OnCloudPropertyChanged;

        // Si el usuario eligió una carpeta en otra sesión, la recuperamos
        SavesPath = _settings.Current.SavesPath ?? _scanner.DefaultSavesPath;
        SearchText = "";
        SelectedTypeFilter = AllTypes;
        SelectedSort = SortRecent;
        StatusText = "";
        TotalSizeText = "";
        LastScanText = "";

        _activity.Start(SavesPath);
    }

    // Si cambia la carpeta saves, el vigilante pasa a vigilar la nueva
    partial void OnSavesPathChanged(string value)
    {
        if (_activity is not null) // en el constructor se asigna SavesPath antes que _activity
            _activity.Start(value);
    }

    /// <summary>
    /// Escaneamos cada vez que se entra en la página (también la primera, al arrancar):
    /// así se ven los cambios hechos desde otras páginas, como restaurar una copia.
    /// </summary>
    public override void OnNavigatedTo() => _ = RefreshAsync();

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
    private async Task CreateBackupAsync()
    {
        if (SelectedWorld is not { } world)
            return;

        // El aviso de abajo a la derecha muestra el progreso mientras se comprime
        var progress = _toast.Start("Creando copia de seguridad", world.Name);
        try
        {
            var backup = await _backupService.CreateBackupAsync(world.FolderPath, progress: progress);
            _toast.Succeed($"Copia guardada ({Formatters.Size(backup.SizeBytes)})");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Incluye WorldInUseException: el mundo está abierto en Minecraft
            _toast.Fail(e.Message);
        }
    }

    /// <summary>
    /// Sube un mundo. Lo usan "Sincronizar ahora" (con el mundo seleccionado) y los botones
    /// de cada tarjeta ("Habilitar Cloud Sync", "Subir cambios"), que pasan su propio mundo.
    /// </summary>
    [RelayCommand]
    private async Task SyncWorldAsync(WorldItemViewModel? world)
    {
        if (world is null)
            return;

        // Sin cuenta conectada, llevamos al usuario a conectarla
        if (!_cloud.IsSignedIn)
        {
            _toast.Fail($"Conecta tu cuenta de {_cloud.ProviderName} para sincronizar.");
            _navigation.NavigateTo(_cloudPage);
            return;
        }

        await UploadCoreAsync(world, $"Sincronizando con {_cloud.ProviderName}");
    }

    private async Task<bool> UploadCoreAsync(WorldItemViewModel world, string toastTitle)
    {
        var progress = _toast.Start(toastTitle, world.Name);
        try
        {
            var uploaded = await _sync.UploadWorldAsync(world.FolderPath, progress);
            _toast.Succeed($"Subido a {_cloud.ProviderName} ({Formatters.Size(uploaded.SizeBytes)})");
            _cloud.RefreshCommand.Execute(null); // el espacio usado ha cambiado

            // Lo acabamos de subir: está sincronizado
            world.ApplySyncStatus(new WorldSyncStatus(SyncState.Synced, uploaded, 0));
            UpdateSyncSummary();
            return true;
        }
        catch (Exception e) when (e is CloudException or IOException or UnauthorizedAccessException)
        {
            _toast.Fail(e.Message);
            return false;
        }
    }

    /// <summary>
    /// "Sincronizar todo": sube los mundos con cambios y baja las versiones más nuevas de la nube.
    /// Los conflictos se saltan porque necesitan que el usuario elija.
    /// </summary>
    [RelayCommand]
    private async Task SyncAllAsync()
    {
        if (!_cloud.IsSignedIn)
        {
            _toast.Fail($"Conecta tu cuenta de {_cloud.ProviderName} para sincronizar.");
            _navigation.NavigateTo(_cloudPage);
            return;
        }

        var toUpload = _allWorlds.Where(w => w.HasPendingChanges && !w.IsInUse).ToList();
        var toDownload = _allWorlds.Where(w => w.IsCloudNewer && !w.IsInUse).ToList();
        var conflicts = _allWorlds.Count(w => w.HasConflict);

        if (toUpload.Count + toDownload.Count == 0)
        {
            _toast.Inform("Sincronizar todo", "Nada que hacer",
                conflicts > 0 ? $"Todo al día salvo {conflicts} conflicto(s): resuélvelos con «Resolver»." : "Todos los mundos están al día.");
            return;
        }

        // Descargar sustituye mundos: lo confirmamos (con todo lo que se va a hacer)
        if (toDownload.Count > 0)
        {
            var message = $"Se subirán {toUpload.Count} mundo(s) y se descargarán {toDownload.Count}: "
                + string.Join(", ", toDownload.Select(w => w.Name)) + ".\n\n"
                + "Antes de sustituir cada mundo se guarda una copia de su estado actual en Copias de Seguridad.";
            if (conflicts > 0)
                message += $"\n\n{conflicts} conflicto(s) se saltarán: resuélvelos uno a uno.";

            var confirmed = await _dialogs.ConfirmAsync(new ConfirmOptions(
                Title: "¿Sincronizar todos los mundos?",
                Message: message,
                ConfirmText: "Sincronizar todo",
                Icon: "cloud_sync"));
            if (!confirmed)
                return;
        }

        var done = 0;
        var total = toUpload.Count + toDownload.Count;
        foreach (var world in toUpload)
            if (await UploadCoreAsync(world, $"Sincronizando todo ({++done}/{total})"))
                continue;
            else
                return; // si falla una, paramos (el aviso ya muestra el error)

        foreach (var world in toDownload)
        {
            var progress = _toast.Start($"Sincronizando todo ({++done}/{total})", world.Name);
            try
            {
                await _sync.DownloadWorldAsync(world.LatestCloudBackup!, SavesPath, progress);
            }
            catch (Exception e) when (e is CloudException or IOException or InvalidDataException or UnauthorizedAccessException)
            {
                _toast.Fail(e.Message);
                await RefreshAsync();
                return;
            }
        }

        _toast.Succeed(total == 1 ? "1 mundo sincronizado" : $"{total} mundos sincronizados");
        if (toDownload.Count > 0)
            await RefreshAsync();
    }

    private void OnWorldOpened(string worldFolder)
    {
        if (FindWorld(worldFolder) is { } world)
            world.IsInUse = true;
    }

    /// <summary>
    /// Has salido de un mundo en Minecraft: volvemos a escanear (puede haber cambiado su tamaño,
    /// o ser un mundo nuevo) y, si tiene cambios y ya estaba en la nube, lo subimos solo.
    /// </summary>
    private async Task OnWorldClosedAsync(string worldFolder)
    {
        if (FindWorld(worldFolder) is { } closing)
            closing.IsInUse = false;

        // Minecraft suelta session.lock al final de guardar; damos un margen por si acaso
        await Task.Delay(TimeSpan.FromSeconds(3));
        await RefreshAsync();

        if (!_cloud.IsSignedIn || FindWorld(worldFolder) is not { } world)
            return;

        if (world.HasPendingChanges && _settings.Current.AutoUploadOnWorldClose)
            await UploadCoreAsync(world, "Subida automática");
        else if (world.HasConflict)
            _toast.Fail($"«{world.Name}» ha cambiado aquí y en la nube. Resuélvelo en Mis Mundos.");
    }

    private WorldItemViewModel? FindWorld(string worldFolder)
        => _allWorlds.FirstOrDefault(w => !w.IsCloudOnly
            && string.Equals(Path.GetFullPath(w.FolderPath), Path.GetFullPath(worldFolder), StringComparison.OrdinalIgnoreCase));

    /// <summary>"Sincronizar ahora" de la barra inferior: hace lo que toque según el estado del mundo.</summary>
    [RelayCommand]
    private Task SyncSelectedAsync()
    {
        if (SelectedWorld is not { } world)
            return Task.CompletedTask;

        return world switch
        {
            { IsCloudSide: true } => DownloadWorldAsync(world),
            { HasConflict: true } => ResolveConflictAsync(world),
            _ => SyncWorldAsync(world),
        };
    }

    /// <summary>"Descargar" (mundo solo en la nube) o "Descargar cambios" (versión más nueva en la nube).</summary>
    [RelayCommand]
    private async Task DownloadWorldAsync(WorldItemViewModel? world)
    {
        if (world?.LatestCloudBackup is not { } backup)
            return;

        // Si el mundo ya está en este PC, se va a sustituir: pedimos confirmación
        if (!world.IsCloudOnly)
        {
            var confirmed = await _dialogs.ConfirmAsync(new ConfirmOptions(
                Title: "¿Descargar la versión de la nube?",
                Message: $"«{world.Name}» se sustituirá por la versión subida {Formatters.RelativeDate(backup.CreatedAt).ToLowerInvariant()}.\n\n"
                    + "Antes se guardará una copia del estado actual en Copias de Seguridad, así que podrás deshacerlo.",
                ConfirmText: "Descargar",
                Icon: "cloud_download"));
            if (!confirmed)
                return;
        }

        await DownloadCoreAsync(world.Name, backup);
    }

    /// <summary>El mundo cambió aquí y en la nube: el usuario elige qué versión conservar.</summary>
    [RelayCommand]
    private async Task ResolveConflictAsync(WorldItemViewModel? world)
    {
        if (world?.LatestCloudBackup is not { } backup)
            return;

        var choice = await _dialogs.ChooseAsync(new ConfirmOptions(
            Title: "Este mundo ha cambiado en los dos sitios",
            Message: $"Has jugado a «{world.Name}» en este PC y, además, en la nube hay una versión subida "
                + $"{Formatters.RelativeDate(backup.CreatedAt).ToLowerInvariant()} desde otro sitio.\n\n"
                + "Elige cuál quieres conservar. La otra no se pierde: queda guardada como copia "
                + "(en la nube o en Copias de Seguridad).",
            ConfirmText: "Subir la de este PC",
            Icon: "call_split",
            AlternativeText: "Usar la de la nube"));

        switch (choice)
        {
            case DialogChoice.Confirm:
                await SyncWorldAsync(world);
                break;
            case DialogChoice.Alternative:
                await DownloadCoreAsync(world.Name, backup);
                break;
        }
    }

    private async Task DownloadCoreAsync(string worldName, CloudBackup backup)
    {
        var progress = _toast.Start($"Descargando de {_cloud.ProviderName}", worldName);
        try
        {
            await _sync.DownloadWorldAsync(backup, SavesPath, progress);
            _toast.Succeed("Mundo descargado");
        }
        catch (Exception e) when (e is CloudException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _toast.Fail(e.Message);
        }

        // El mundo ha cambiado (o es nuevo): volvemos a escanear
        await RefreshAsync();
    }

    [RelayCommand]
    private void ShowBackups()
    {
        // Llevamos al usuario a Copias de Seguridad, ya filtrado por este mundo
        if (SelectedWorld is { } world)
            _backupsPage.ShowWorld(world.FolderName);

        _navigation.NavigateTo(_backupsPage);
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
            _allWorlds = worlds.Select(w => new WorldItemViewModel(w, _folderLauncher, _clipboard)
            {
                IsInUse = _activity.IsOpen(w.FolderPath),
            }).ToList();
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

        // Segunda fase: la lista ya se ve; ahora comparamos cada mundo con la nube
        await CheckSyncStatusAsync();
    }

    /// <summary>Pide la lista de la nube una sola vez y calcula el estado de cada mundo.</summary>
    private async Task CheckSyncStatusAsync()
    {
        // Si se está escaneando, la lista de mundos aún está vacía: compararla con la nube haría
        // creer que todos los mundos de la nube faltan en este PC. RefreshAsync ya comprobará
        // la nube cuando termine el escaneo (pasa al arrancar: la sesión se recupera a la vez).
        if (IsLoading)
            return;

        var version = ++_syncCheckVersion;
        var worlds = _allWorlds.Where(w => !w.IsCloudOnly).ToList();

        if (!_cloud.IsSignedIn)
        {
            foreach (var world in worlds)
                world.ApplySyncStatus(null); // "Nube sin conectar"
            SetCloudOnlyWorlds(worlds, []); // sin cuenta no sabemos qué hay en la nube
            UpdateSyncSummary();
            return;
        }

        foreach (var world in worlds)
            world.IsCheckingSync = true;

        try
        {
            var cloudBackups = await _cloudProvider.ListBackupsAsync();
            foreach (var world in worlds)
            {
                var status = await _sync.GetStatusAsync(world.FolderPath, cloudBackups);
                if (version != _syncCheckVersion)
                    return; // ha empezado otra comprobación más reciente

                world.ApplySyncStatus(status);
            }

            // Mundos que están en la nube pero no en este PC
            var cloudOnly = _sync.GetCloudOnlyWorlds(cloudBackups, worlds.Select(w => w.FolderName))
                .Select(b => WorldItemViewModel.FromCloud(b, SavesPath, _folderLauncher, _clipboard))
                .ToList();
            SetCloudOnlyWorlds(worlds, cloudOnly);
            ShowStartupNotice();
        }
        catch (Exception e) when (e is CloudException or IOException or UnauthorizedAccessException)
        {
            foreach (var world in worlds)
                world.ApplySyncStatus(null);
            _toast.Fail($"No se pudo comprobar la nube: {e.Message}");
        }

        UpdateSyncSummary();
    }

    private void OnCloudPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Solo nos interesa cuando cambia la cuenta (conectar/desconectar), no la cuota
        if (e.PropertyName == nameof(CloudSessionViewModel.IsSignedIn))
            _ = CheckSyncStatusAsync();
    }

    /// <summary>La primera vez que se comprueba la nube, avisamos si hay algo que hacer.</summary>
    private void ShowStartupNotice()
    {
        if (_startupNoticeDone)
            return;
        _startupNoticeDone = true;

        if (!_settings.Current.NotifyCloudChangesOnStartup)
            return;

        var newer = _allWorlds.Count(w => w.IsCloudNewer);
        var cloudOnly = _allWorlds.Count(w => w.IsCloudOnly);
        var conflicts = _allWorlds.Count(w => w.HasConflict);

        var parts = new List<string>();
        if (newer > 0) parts.Add(newer == 1 ? "1 mundo con versión más nueva" : $"{newer} mundos con versión más nueva");
        if (cloudOnly > 0) parts.Add(cloudOnly == 1 ? "1 mundo que no está en este PC" : $"{cloudOnly} mundos que no están en este PC");
        if (conflicts > 0) parts.Add(conflicts == 1 ? "1 conflicto" : $"{conflicts} conflictos");

        if (parts.Count > 0)
            _toast.Inform($"Novedades en {_cloud.ProviderName}", string.Join(" · ", parts),
                "Usa «Sincronizar todo» o los botones de cada mundo.");
    }

    private void SetCloudOnlyWorlds(List<WorldItemViewModel> localWorlds, List<WorldItemViewModel> cloudOnly)
    {
        if (cloudOnly.Count == 0 && _allWorlds.All(w => !w.IsCloudOnly))
            return; // nada que cambiar en la lista

        _allWorlds = [.. localWorlds, .. cloudOnly];
        ApplyFilter();
    }

    private void UpdateSyncSummary()
    {
        var local = _allWorlds.Where(w => !w.IsCloudOnly).ToList();
        var synced = local.Count(w => w.IsSynced);
        var cloudOnly = _allWorlds.Count - local.Count;

        SyncSummaryText = !_cloud.IsSignedIn ? ""
            : cloudOnly == 0 ? $"{synced} de {local.Count} sincronizados"
            : $"{synced} de {local.Count} sincronizados · {cloudOnly} solo en la nube";
        _cloud.SyncedWorldsCount = _cloud.IsSignedIn ? synced : null;
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
        // Contamos solo los mundos de este PC (no las tarjetas "Solo en la nube")
        var local = _allWorlds.Count(w => !w.IsCloudOnly);
        var visible = Worlds.Count(w => !w.IsCloudOnly);
        TotalSizeText = $"{Formatters.Size(_allWorlds.Where(w => !w.IsCloudOnly).Sum(w => w.SizeBytes))} ocupados";

        StatusText = IsLoading ? "Buscando mundos…"
            : _scanError ?? (visible == local
                ? $"{local} Mundos detectados"
                : $"{visible} de {local} Mundos");
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
