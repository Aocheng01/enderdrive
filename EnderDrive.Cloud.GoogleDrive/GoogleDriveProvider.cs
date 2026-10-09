using EnderDrive.Core.Cloud;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;

namespace EnderDrive.Cloud.GoogleDrive;

/// <summary>
/// Google Drive con OAuth 2.0 para apps de escritorio:
/// <list type="number">
/// <item>Se abre el navegador con la pantalla de permisos de Google.</item>
/// <item>Google devuelve un código a un pequeño servidor local (127.0.0.1) que abre la librería.</item>
/// <item>Ese código se cambia por un token, que guardamos para no pedir permiso cada vez.</item>
/// </list>
/// Este archivo tiene el inicio de sesión; la subida y lista de copias está en
/// GoogleDriveProvider.Files.cs (las dos partes forman una sola clase gracias a "partial").
/// </summary>
public sealed partial class GoogleDriveProvider : ICloudProvider
{
    // Clave con la que se guarda el token. Solo hay una cuenta conectada a la vez.
    private const string UserKey = "user";

    // drive.file: la app solo puede ver los archivos que ella misma ha creado,
    // no el resto del Drive del usuario. Es el permiso más pequeño que nos sirve.
    private static readonly string[] Scopes = [DriveService.Scope.DriveFile];

    private readonly string _clientSecretsPath;
    private readonly FileDataStore _tokenStore;
    private UserCredential? _credential;
    private DriveService? _drive;

    /// <param name="clientSecretsPath">El .json de credenciales descargado de Google Cloud Console.</param>
    /// <param name="tokenFolder">Carpeta donde se guarda el token del usuario.</param>
    public GoogleDriveProvider(string clientSecretsPath, string tokenFolder)
    {
        _clientSecretsPath = clientSecretsPath;
        _tokenStore = new FileDataStore(tokenFolder, fullPath: true);
    }

    public string Id => "google-drive";

    public string DisplayName => "Google Drive";

    public bool IsConfigured => File.Exists(_clientSecretsPath);

    public bool IsSignedIn => _drive is not null;

    public async Task<CloudAccount?> RestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            return null;

        var flow = CreateFlow();
        var token = await flow.LoadTokenAsync(UserKey, cancellationToken);
        if (token is null)
            return null; // nunca se ha iniciado sesión

        Connect(new UserCredential(flow, UserKey, token));
        try
        {
            return await GetAccountAsync(cancellationToken);
        }
        catch (CloudException e) when (e.InnerException is TokenResponseException)
        {
            // El permiso se revocó o caducó (en modo prueba, Google lo caduca a los 7 días)
            await ForgetSessionAsync();
            return null;
        }
    }

    public async Task<CloudAccount> SignInAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            throw new CloudNotConfiguredException(
                $"Falta el archivo de credenciales de Google: {_clientSecretsPath}");

        // Abre el navegador y espera a que el usuario acepte (o a que se cancele).
        // El "code receiver" es el servidor local que recibe la respuesta de Google;
        // le damos nuestra página en lugar del texto en blanco que trae por defecto.
        var codeReceiver = new LocalServerCodeReceiver(LoadAuthCompletePage());
        var credential = await Translate(() => GoogleWebAuthorizationBroker.AuthorizeAsync(
            LoadClientSecrets(), Scopes, UserKey, cancellationToken, _tokenStore, codeReceiver));

        Connect(credential);
        return await GetAccountAsync(cancellationToken);
    }

    public async Task<CloudAccount> GetAccountAsync(CancellationToken cancellationToken = default)
    {
        var drive = RequireDrive();

        // "about" devuelve datos de la cuenta. Con Fields pedimos solo lo que necesitamos.
        var request = drive.About.Get();
        request.Fields = "user(displayName,emailAddress),storageQuota(limit,usage)";
        var about = await Translate(() => request.ExecuteAsync(cancellationToken));

        return new CloudAccount(
            DisplayName: about.User?.DisplayName ?? "",
            Email: about.User?.EmailAddress ?? "",
            Quota: new CloudQuota(about.StorageQuota?.Usage ?? 0, about.StorageQuota?.Limit));
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        if (_credential is not null)
        {
            try
            {
                // Le decimos a Google que retire el permiso a la app
                await _credential.RevokeTokenAsync(cancellationToken);
            }
            catch (Exception e) when (e is TokenResponseException or HttpRequestException)
            {
                // Sin conexión o ya revocado: igualmente borramos la sesión local
            }
        }

        await ForgetSessionAsync();
    }

    private DriveService RequireDrive()
        => _drive ?? throw new CloudException("No hay ninguna cuenta de Google Drive conectada.");

    private static Task Translate(Func<Task> action)
        => Translate(async () => { await action(); return true; });

    /// <summary>Convierte los errores de Google en CloudException (ver ICloudProvider).</summary>
    private static async Task<T> Translate<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (TokenResponseException e)
        {
            throw new CloudException($"Google rechazó el permiso: {e.Error?.ErrorDescription ?? e.Message}", e);
        }
        catch (Google.GoogleApiException e)
        {
            throw new CloudException($"Error de Google Drive: {e.Message}", e);
        }
        catch (HttpRequestException e)
        {
            throw new CloudException("No hay conexión con Google. Comprueba tu conexión a internet.", e);
        }
    }

    private void Connect(UserCredential credential)
    {
        _drive?.Dispose();
        _credential = credential;
        _drive = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential, // añade el token a cada petición y lo renueva solo
            ApplicationName = "EnderDrive",
        });
    }

    private async Task ForgetSessionAsync()
    {
        _drive?.Dispose();
        _drive = null;
        _credential = null;
        _folderIds.Clear();
        await _tokenStore.ClearAsync();
    }

    /// <summary>Lee la página HTML que va incrustada en la .dll (ver el .csproj).</summary>
    private static string LoadAuthCompletePage()
    {
        using var stream = typeof(GoogleDriveProvider).Assembly.GetManifestResourceStream("EnderDrive.AuthComplete.html")
            ?? throw new InvalidOperationException("Falta el recurso EnderDrive.AuthComplete.html");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private ClientSecrets LoadClientSecrets()
    {
        try
        {
            return GoogleClientSecrets.FromFile(_clientSecretsPath).Secrets
                ?? throw new InvalidDataException("no contiene la sección \"installed\"");
        }
        catch (Exception e) when (e is not CloudException)
        {
            throw new CloudNotConfiguredException(
                $"El archivo de credenciales de Google no es válido ({_clientSecretsPath}): {e.Message}");
        }
    }

    private GoogleAuthorizationCodeFlow CreateFlow() => new(new GoogleAuthorizationCodeFlow.Initializer
    {
        ClientSecrets = LoadClientSecrets(),
        Scopes = Scopes,
        DataStore = _tokenStore,
    });
}
