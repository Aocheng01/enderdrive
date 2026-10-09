namespace EnderDrive.Core.Models;

/// <summary>Por qué se creó una copia.</summary>
public enum BackupReason
{
    /// <summary>La pidió el usuario.</summary>
    Manual,

    /// <summary>Automática: el estado del mundo justo antes de restaurar otra copia.</summary>
    BeforeRestore,
}

/// <summary>Una copia de seguridad (.zip) de un mundo.</summary>
public record BackupInfo(
    string WorldFolderName,
    string FilePath,
    DateTime CreatedAt,
    long SizeBytes,
    BackupReason Reason);

/// <summary>Progreso de una operación larga (copiar, restaurar…).</summary>
public record OperationProgress(string Stage, long Done, long Total)
{
    public double Percent => Total <= 0 ? 0 : Math.Min(100, Done * 100.0 / Total);
}
