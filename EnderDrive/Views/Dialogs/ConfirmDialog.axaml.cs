using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace EnderDrive.Views.Dialogs
{
    /// <summary>
    /// Diálogo sencillo de Sí/No. No tiene ViewModel propio: sus textos vienen de
    /// <see cref="ConfirmOptions"/> (el DataContext) y el resultado se devuelve con Close(true/false).
    /// </summary>
    public partial class ConfirmDialog : Window
    {
        public ConfirmDialog()
        {
            InitializeComponent();
        }

        private void Confirm_Click(object? sender, RoutedEventArgs e) => Close(true);

        private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            // Sin barra de título, dejamos arrastrar el diálogo desde cualquier zona vacía
            base.OnPointerPressed(e);
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        }
    }
}
