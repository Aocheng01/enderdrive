namespace EnderDrive.Core.Cloud;

/// <summary>
/// Lo que cualquier nube (Google Drive, Dropbox, OneDrive…) tiene que saber hacer.
/// La app solo habla con esta interfaz, así que añadir otra nube consiste en
/// escribir otra clase que la implemente, sin tocar las pantallas.
/// </summary>
public interface ICloudProvider
{
    /// <summary>Identificador interno, p. ej. "google-drive".</summary>
    string Id { get; }

    /// <summary>Nombre para mostrar, p. ej. "Google Drive".</summary>
    string DisplayName { get; }

    /// <summary>false si faltan las credenciales de la app (el archivo de OAuth del desarrollador).</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Recupera la sesión guardada de una vez anterior, sin abrir el navegador.
    /// Devuelve null si no hay sesión o si ha caducado.
    /// </summary>
    Task<CloudAccount?> RestoreSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>Abre el navegador para que el usuario inicie sesión y dé permiso.</summary>
    /// <exception cref="CloudNotConfiguredException">Si faltan las credenciales de la app.</exception>
    Task<CloudAccount> SignInAsync(CancellationToken cancellationToken = default);

    /// <summary>Vuelve a pedir los datos de la cuenta (por ejemplo, el espacio usado).</summary>
    Task<CloudAccount> GetAccountAsync(CancellationToken cancellationToken = default);

    /// <summary>Cierra la sesión, revoca el permiso y borra el token guardado.</summary>
    Task SignOutAsync(CancellationToken cancellationToken = default);
}

/// <summary>La cuenta conectada.</summary>
public record CloudAccount(string DisplayName, string Email, CloudQuota Quota);

/// <summary>Espacio de almacenamiento. LimitBytes es null si la cuenta no tiene límite.</summary>
public record CloudQuota(long UsedBytes, long? LimitBytes)
{
    public double? Percent => LimitBytes is > 0 ? UsedBytes * 100.0 / LimitBytes.Value : null;
}

/// <summary>
/// Error al hablar con la nube (sin conexión, permiso revocado…). Cada proveedor convierte
/// sus propias excepciones en esta, para que la app no dependa de ninguna librería concreta.
/// </summary>
public class CloudException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Falta el archivo con las credenciales OAuth de la app.</summary>
public sealed class CloudNotConfiguredException(string message) : CloudException(message);
