namespace EnderDrive.Views.Dialogs
{
    /// <summary>Textos del diálogo de confirmación. IsDestructive pinta el botón en rojo.</summary>
    public sealed record ConfirmOptions(
        string Title,
        string Message,
        string ConfirmText,
        string Icon = "help",
        bool IsDestructive = false,
        string CancelText = "Cancelar");
}
