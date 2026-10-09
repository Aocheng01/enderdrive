using System.Threading.Tasks;
using EnderDrive.Views.Dialogs;

namespace EnderDrive.Services
{
    public interface IDialogService
    {
        /// <summary>Muestra una ventana de confirmación. Devuelve true si el usuario acepta.</summary>
        Task<bool> ConfirmAsync(ConfirmOptions options);
    }

    public sealed class DialogService : IDialogService
    {
        public async Task<bool> ConfirmAsync(ConfirmOptions options)
        {
            if (MainWindowLocator.Get() is not { } owner)
                return false;

            // ShowDialog bloquea la ventana principal hasta que se cierra el diálogo
            var dialog = new ConfirmDialog { DataContext = options };
            return await dialog.ShowDialog<bool>(owner);
        }
    }
}
