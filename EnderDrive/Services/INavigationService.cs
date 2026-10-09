using System;
using EnderDrive.ViewModels;

namespace EnderDrive.Services
{
    /// <summary>
    /// Permite que una página pida ir a otra (por ejemplo, "Restaurar versión anterior"
    /// lleva a Copias de Seguridad) sin conocer el MainViewModel.
    /// </summary>
    public interface INavigationService
    {
        event Action<ViewModelBase>? NavigationRequested;

        void NavigateTo(ViewModelBase page);
    }

    public sealed class NavigationService : INavigationService
    {
        public event Action<ViewModelBase>? NavigationRequested;

        public void NavigateTo(ViewModelBase page) => NavigationRequested?.Invoke(page);
    }
}
