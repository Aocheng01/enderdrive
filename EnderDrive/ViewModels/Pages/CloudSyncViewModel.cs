namespace EnderDrive.ViewModels.Pages
{
    /// <summary>
    /// Página "Sincronización en la Nube". De momento solo gestiona la conexión de la cuenta;
    /// todo el estado vive en CloudSessionViewModel, que se comparte con el resto de la app.
    /// </summary>
    public partial class CloudSyncViewModel : ViewModelBase
    {
        public string Title => "Sincronización en la Nube";

        public CloudSessionViewModel Cloud { get; }

        /// <summary>Nombre del archivo que el desarrollador tiene que colocar junto al .exe.</summary>
        public string CredentialsFileName => App.GoogleCredentialsFileName;

        public CloudSyncViewModel(CloudSessionViewModel cloud)
        {
            Cloud = cloud;
        }

        /// <summary>Al entrar en la página, actualizamos el espacio usado.</summary>
        public override void OnNavigatedTo()
        {
            if (Cloud.IsSignedIn)
                Cloud.RefreshCommand.Execute(null);
        }
    }
}
