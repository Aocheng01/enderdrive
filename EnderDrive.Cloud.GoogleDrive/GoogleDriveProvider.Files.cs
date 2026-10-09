using System.Globalization;
using EnderDrive.Core.Cloud;
using EnderDrive.Core.Models;
using Google.Apis.Upload;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace EnderDrive.Cloud.GoogleDrive;

/// <summary>
/// Copias en Drive. Estructura:
/// <c>Mi unidad / EnderDrive / {carpeta del mundo} / 2026-10-09_18-30-05.zip</c>
/// Cada .zip lleva "appProperties": etiquetas invisibles para el usuario con el mundo,
/// la fecha y el tipo de copia. Así podemos buscarlas sin depender del nombre del archivo.
/// </summary>
public sealed partial class GoogleDriveProvider
{
    private const string RootFolderName = "EnderDrive";
    private const string FolderMimeType = "application/vnd.google-apps.folder";
    private const string ZipMimeType = "application/zip";

    // Campos que pedimos de cada archivo (Drive solo devuelve lo que se pide)
    private const string FileFields = "id,name,size,appProperties";

    // Etiquetas (appProperties) de nuestras copias
    private const string KindKey = "enderdrive";
    private const string KindBackup = "backup";
    private const string WorldKey = "world";
    private const string CreatedAtKey = "createdAt";
    private const string ReasonKey = "reason";

    // Ids de carpetas ya buscadas, para no preguntar a Drive en cada subida
    private readonly Dictionary<string, string> _folderIds = new();

    public async Task<CloudBackup> UploadBackupAsync(
        BackupInfo backup,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var drive = RequireDrive();
        var rootId = await GetOrCreateFolderAsync(RootFolderName, parentId: null, cancellationToken);
        var worldFolderId = await GetOrCreateFolderAsync(backup.WorldFolderName, rootId, cancellationToken);

        var metadata = new DriveFile
        {
            Name = Path.GetFileName(backup.FilePath),
            Parents = [worldFolderId],
            MimeType = ZipMimeType,
            AppProperties = new Dictionary<string, string>
            {
                [KindKey] = KindBackup,
                [WorldKey] = backup.WorldFolderName,
                [CreatedAtKey] = backup.CreatedAt.ToString("o", CultureInfo.InvariantCulture),
                [ReasonKey] = backup.Reason.ToString(),
            },
        };

        await using var stream = File.OpenRead(backup.FilePath);
        var total = stream.Length;

        // Subida "reanudable": el archivo se envía por trozos. Si un trozo falla, la librería
        // lo reintenta, y entre trozo y trozo nos avisa de cuánto lleva enviado.
        var request = drive.Files.Create(metadata, stream, ZipMimeType);
        request.Fields = FileFields;
        request.ChunkSize = ResumableUpload.MinimumChunkSize * 4; // 1 MB: progreso más fluido
        request.ProgressChanged += p =>
            progress?.Report(new OperationProgress("Subiendo a Google Drive", p.BytesSent, total));

        progress?.Report(new OperationProgress("Subiendo a Google Drive", 0, total));
        var result = await Translate(() => request.UploadAsync(cancellationToken));

        if (result.Status != UploadStatus.Completed || request.ResponseBody is null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new CloudException($"No se pudo subir la copia: {result.Exception?.Message}", result.Exception);
        }

        return ToCloudBackup(request.ResponseBody)
            ?? throw new CloudException("Drive no devolvió los datos del archivo subido.");
    }

    public async Task<IReadOnlyList<CloudBackup>> ListBackupsAsync(CancellationToken cancellationToken = default)
    {
        var drive = RequireDrive();
        var backups = new List<CloudBackup>();
        string? pageToken = null;

        // Drive devuelve los resultados por páginas: pedimos hasta que no haya más
        do
        {
            var request = drive.Files.List();
            request.Q = $"appProperties has {{ key='{KindKey}' and value='{KindBackup}' }} and trashed = false";
            request.Fields = $"nextPageToken, files({FileFields})";
            request.PageSize = 1000;
            request.PageToken = pageToken;

            var page = await Translate(() => request.ExecuteAsync(cancellationToken));
            backups.AddRange(page.Files.Select(ToCloudBackup).OfType<CloudBackup>());
            pageToken = page.NextPageToken;
        }
        while (pageToken is not null);

        return backups.OrderByDescending(b => b.CreatedAt).ToList();
    }

    public Task DeleteBackupAsync(CloudBackup backup, CancellationToken cancellationToken = default)
    {
        // A la papelera, no borrado definitivo: el usuario puede recuperarla durante 30 días
        var request = RequireDrive().Files.Update(new DriveFile { Trashed = true }, backup.Id);
        return Translate(() => request.ExecuteAsync(cancellationToken));
    }

    private async Task<string> GetOrCreateFolderAsync(string name, string? parentId, CancellationToken cancellationToken)
    {
        var cacheKey = $"{parentId}/{name}";
        if (_folderIds.TryGetValue(cacheKey, out var cached))
            return cached;

        var drive = RequireDrive();

        // Con el permiso drive.file solo vemos las carpetas que creó EnderDrive
        var list = drive.Files.List();
        list.Q = $"mimeType = '{FolderMimeType}' and name = '{EscapeQuery(name)}' and trashed = false"
            + $" and '{parentId ?? "root"}' in parents";
        list.Fields = "files(id)";
        var existing = await Translate(() => list.ExecuteAsync(cancellationToken));

        var id = existing.Files.FirstOrDefault()?.Id;
        if (id is null)
        {
            var create = drive.Files.Create(new DriveFile
            {
                Name = name,
                MimeType = FolderMimeType,
                Parents = parentId is null ? null : [parentId],
            });
            create.Fields = "id";
            id = (await Translate(() => create.ExecuteAsync(cancellationToken))).Id;
        }

        _folderIds[cacheKey] = id;
        return id;
    }

    /// <summary>Pasa un archivo de Drive a nuestro modelo. null si le faltan las etiquetas.</summary>
    private static CloudBackup? ToCloudBackup(DriveFile file)
    {
        var props = file.AppProperties;
        if (props is null || !props.TryGetValue(WorldKey, out var world))
            return null;

        var createdAt = props.TryGetValue(CreatedAtKey, out var created)
            && DateTime.TryParse(created, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed.ToLocalTime()
                : DateTime.MinValue;

        var reason = props.TryGetValue(ReasonKey, out var r) && Enum.TryParse<BackupReason>(r, out var parsedReason)
            ? parsedReason
            : BackupReason.Manual;

        return new CloudBackup(file.Id, world, file.Name, createdAt, file.Size ?? 0, reason);
    }

    /// <summary>En las búsquedas de Drive, las comillas y barras del nombre hay que escaparlas.</summary>
    private static string EscapeQuery(string value) => value.Replace("\\", "\\\\").Replace("'", "\\'");
}
