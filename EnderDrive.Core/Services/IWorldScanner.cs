using EnderDrive.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace EnderDrive.Core.Services
{
    public interface IWorldScanner
    {
        string DefaultSavesPath { get; }

        Task<IReadOnlyList<WorldInfo>> ScanAsync(string savesPath, CancellationToken cancellationToken = default);
    }
}
