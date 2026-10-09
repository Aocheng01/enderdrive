using System.Threading.Tasks;
using EnderDrive.Views.Dialogs;

namespace EnderDrive.Services
{
    public interface IDialogService
    {
        /// <summary>Muestra una ventana de confirmación. Devuelve true si el usuario acepta.</summary>
        Task<bool> ConfirmAsync(ConfirmOptions options);

        /// <summary>Como ConfirmAsync, pero con tres posibles respuestas (ver ConfirmOptions.AlternativeText).</summary>
        Task<DialogChoice> ChooseAsync(ConfirmOptions options);
    }

    public sealed class DialogService : IDialogService
    {
        public async Task<bool> ConfirmAsync(ConfirmOptions options)
            => await ChooseAsync(options) == DialogChoice.Confirm;

        public async Task<DialogChoice> ChooseAsync(ConfirmOptions options)
        {
            if (MainWindowLocator.Get() is not { } owner)
                return DialogChoice.Cancel;

            // ShowDialog bloquea la ventana principal hasta que se cierra el diálogo
            var dialog = new ConfirmDialog { DataContext = options };
            return await dialog.ShowDialog<DialogChoice>(owner);
        }
    }
}
