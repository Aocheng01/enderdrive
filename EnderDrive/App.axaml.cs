using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using EnderDrive.ViewModels;
using EnderDrive.ViewModels.Pages;
using EnderDrive.Views;
using Microsoft.Extensions.DependencyInjection;
using EnderDrive.Cloud.GoogleDrive;
using EnderDrive.Core.Cloud;
using EnderDrive.Core.Services;
using EnderDrive.Services;

namespace EnderDrive;

public partial class App : Application
{
    /// <summary>
    /// Credenciales OAuth de la app (las crea el desarrollador en Google Cloud Console).
    /// Van junto al .exe y NO se suben a git: ver google-oauth.example.json.
    /// </summary>
    public const string GoogleCredentialsFileName = "google-oauth.json";

    public IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Services = ConfigureServices();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainViewModel>(),
            };

            // Si el usuario ya conectó su cuenta otra vez, la recuperamos sin abrir el navegador
            _ = Services.GetRequiredService<CloudSessionViewModel>().RestoreAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Servicios del Core
        services.AddSingleton<IWorldScanner, WorldScanner>();
        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<IBackupService, BackupService>();

        // Nube: hoy Google Drive. Para usar otra nube bastaría con registrar otra ICloudProvider.
        services.AddSingleton<ISyncService, SyncService>();
        services.AddSingleton<ISyncStateStore>(_ => new JsonSyncStateStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EnderDrive", "sync-state.json")));
        services.AddSingleton<ICloudProvider>(_ => new GoogleDriveProvider(
            clientSecretsPath: Path.Combine(AppContext.BaseDirectory, GoogleCredentialsFileName),
            tokenFolder: Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "EnderDrive", "google-drive")));

        // Servicios de la app (necesitan la ventana de Avalonia)
        services.AddSingleton<IFolderPicker, FolderPicker>();
        services.AddSingleton<IFolderLauncher, FolderLauncher>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<INavigationService, NavigationService>();

        // ViewModels
        services.AddSingleton<ToastViewModel>();
        services.AddSingleton<CloudSessionViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MyWorldsViewModel>();
        services.AddSingleton<CloudSyncViewModel>();
        services.AddSingleton<BackupsViewModel>();
        services.AddSingleton<SettingsViewModel>();

        return services.BuildServiceProvider();
    }
}