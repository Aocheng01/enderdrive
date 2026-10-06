using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using EnderDrive.ViewModels;
using EnderDrive.ViewModels.Pages;
using EnderDrive.Views;
using Microsoft.Extensions.DependencyInjection;

namespace EnderDrive;

public partial class App : Application
{
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
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Servicios del Core (Fase 2+): services.AddSingleton<IWorldScanner, WorldScanner>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MyWorldsViewModel>();
        services.AddSingleton<CloudSyncViewModel>();
        services.AddSingleton<BackupsViewModel>();
        services.AddSingleton<SettingsViewModel>();

        return services.BuildServiceProvider();
    }
}