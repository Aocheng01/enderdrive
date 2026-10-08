using System;
using System.Collections.Generic;
using System.Text;

namespace EnderDrive.Core.Models
{
    public record WorldInfo(
        string Name,
        string FolderName,
        string FolderPath,
        DateTime LastPlayed,
        long SizeBytes,
        string? IconPath,
        string? GameVersion,
        GameMode? GameMode,
        bool IsHardcore
        );
}
