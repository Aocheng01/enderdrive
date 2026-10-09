using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using EnderDrive.Services;
using EnderDrive.ViewModels.Pages;

namespace EnderDrive.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public IReadOnlyList<NavItem> NavItems { get; }

    [ObservableProperty]
    public partial NavItem? SelectedNavItem { get; set; }

    [ObservableProperty]
    public partial ViewModelBase? CurrentPage { get; set; }

    /// <summary>Aviso flotante de operaciones largas (copias, restauraciones…).</summary>
    public ToastViewModel Toast { get; }

    /// <summary>Cuenta en la nube: alimenta la tarjeta "Capacidad Drive" y el avatar.</summary>
    public CloudSessionViewModel Cloud { get; }

    public MainViewModel(
        MyWorldsViewModel myWorlds,
        CloudSyncViewModel cloudSync,
        BackupsViewModel backups,
        SettingsViewModel settings,
        INavigationService navigation,
        ToastViewModel toast,
        CloudSessionViewModel cloud)
    {
        Toast = toast;
        Cloud = cloud;

        NavItems =
        [
            new NavItem("public", "Mis Mundos", myWorlds),
            new NavItem("sync_saved_locally", "Sincronización en la Nube", cloudSync),
            new NavItem("archive", "Copias de Seguridad", backups),
            new NavItem("tune", "Ajustes", settings),
        ];

        // Cuando una página pide ir a otra, seleccionamos su elemento del menú
        navigation.NavigationRequested += page =>
            SelectedNavItem = NavItems.FirstOrDefault(n => n.Page == page) ?? SelectedNavItem;

        SelectedNavItem = NavItems[0];
    }

    partial void OnSelectedNavItemChanged(NavItem? value)
    {
        if (value is null)
            return;

        CurrentPage = value.Page;
        value.Page.OnNavigatedTo();
    }
}
