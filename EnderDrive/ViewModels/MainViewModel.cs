using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using EnderDrive.ViewModels.Pages;

namespace EnderDrive.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public IReadOnlyList<NavItem> NavItems { get; }

    [ObservableProperty]
    public partial NavItem? SelectedNavItem { get; set; }

    [ObservableProperty]
    public partial ViewModelBase? CurrentPage { get; set; }

    // Datos de prueba para la tarjeta de capacidad (Fase 3: datos reales de Drive)
    public string StorageText => "34.2 / 50 GB";
    public double StoragePercent => 68.4;
    public string SyncedWorldsText => "12 Mundos sincronizados";

    public MainViewModel(
        MyWorldsViewModel myWorlds,
        CloudSyncViewModel cloudSync,
        BackupsViewModel backups,
        SettingsViewModel settings)
    {
        NavItems =
        [
            new NavItem("public", "Mis Mundos", myWorlds),
            new NavItem("sync_saved_locally", "Sincronización en la Nube", cloudSync),
            new NavItem("archive", "Copias de Seguridad", backups),
            new NavItem("tune", "Ajustes", settings),
        ];
        SelectedNavItem = NavItems[0];
    }

    partial void OnSelectedNavItemChanged(NavItem? value)
    {
        if (value is not null)
            CurrentPage = value.Page;
    }
}