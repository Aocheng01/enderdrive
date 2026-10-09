using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace EnderDrive.Views.Dialogs
{
    /// <summary>
    /// Diálogo sencillo de Sí/No. No tiene ViewModel propio: sus textos vienen de
    /// <see cref="ConfirmOptions"/> (el DataContext) y el resultado se devuelve con Close(DialogChoice).
    /// </summary>
    public partial class ConfirmDialog : Window
    {
        public ConfirmDialog()
        {
            InitializeComponent();
        }

        private void Confirm_Click(object? sender, RoutedEventArgs e) => Close(DialogChoice.Confirm);

        private void Alternative_Click(object? sender, RoutedEventArgs e) => Close(DialogChoice.Alternative);

        private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(DialogChoice.Cancel);

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            // Sin barra de título, dejamos arrastrar el diálogo desde cualquier zona vacía
            base.OnPointerPressed(e);
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        }
    }
}
