using System;
using System.Collections.Generic;
using System.Text;

namespace EnderDrive.Core.Models
{
    public record WorldInfo(
        string Name,
        string FolderPath,
        DateTime LastPlayed,
        long SizeBytes,
        string? IconPath
        );
}
