using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnderDrive.Core.Models;

namespace EnderDrive.ViewModels
{
    /// <summary>
    /// El aviso flotante de abajo a la derecha (como el "toast" del diseño de Stitch).
    /// Es único para toda la app: lo usan Mis Mundos y Copias de Seguridad, y sigue visible
    /// aunque cambies de página mientras se crea una copia.
    /// </summary>
    public sealed partial class ToastViewModel : ObservableObject
    {
        // Cada aviso nuevo incrementa este número; así un cierre automático antiguo
        // no oculta un aviso más reciente
        private int _version;

        [ObservableProperty]
        public partial bool IsOpen { get; set; }

        [ObservableProperty]
        public partial string Title { get; set; } = "";

        [ObservableProperty]
        public partial string Subject { get; set; } = "";

        [ObservableProperty]
        public partial string StageText { get; set; } = "";

        [ObservableProperty]
        public partial string ProgressText { get; set; } = "";

        [ObservableProperty]
        public partial double Progress { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Icon))]
        public partial bool IsRunning { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Icon))]
        public partial bool IsError { get; set; }

        public string Icon => IsRunning ? "sync" : IsError ? "error" : "check_circle";

        /// <summary>Muestra el aviso con barra de progreso y devuelve el IProgress que la actualiza.</summary>
        public IProgress<OperationProgress> Start(string title, string subject)
        {
            _version++;
            Title = title;
            Subject = subject;
            StageText = "Preparando…";
            ProgressText = "";
            Progress = 0;
            IsError = false;
            IsRunning = true;
            IsOpen = true;

            // Progress<T> recuerda el hilo donde se creó (el de la interfaz) y ejecuta
            // Report en él, aunque la copia se esté haciendo en otro hilo
            return new Progress<OperationProgress>(p =>
            {
                StageText = p.Stage;
                Progress = p.Percent;
                ProgressText = $"{Formatters.Size(p.Done)} / {Formatters.Size(p.Total)} ({p.Percent:0}%)";
            });
        }

        public void Succeed(string message) => Finish(message, isError: false, TimeSpan.FromSeconds(4));

        public void Fail(string message) => Finish(message, isError: true, TimeSpan.FromSeconds(10));

        [RelayCommand]
        private void Close() => IsOpen = false;

        private async void Finish(string message, bool isError, TimeSpan visibleFor)
        {
            var version = ++_version;
            IsRunning = false;
            IsError = isError;
            Progress = isError ? Progress : 100;
            StageText = message;
            ProgressText = "";

            await Task.Delay(visibleFor);
            if (version == _version)
                IsOpen = false;
        }
    }
}
