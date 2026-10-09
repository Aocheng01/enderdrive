namespace EnderDrive.Views.Dialogs
{
    /// <summary>
    /// Textos del diálogo de confirmación. IsDestructive pinta el botón en rojo.
    /// Si AlternativeText tiene texto, aparece un tercer botón (ver DialogChoice).
    /// </summary>
    public sealed record ConfirmOptions(
        string Title,
        string Message,
        string ConfirmText,
        string Icon = "help",
        bool IsDestructive = false,
        string CancelText = "Cancelar",
        string? AlternativeText = null)
    {
        public bool HasAlternative => AlternativeText is not null;
    }

    /// <summary>Qué botón pulsó el usuario. Cancel es 0: también es el valor si se cierra con Esc.</summary>
    public enum DialogChoice
    {
        Cancel,
        Confirm,
        Alternative,
    }
}
