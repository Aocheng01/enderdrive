using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace EnderDrive.Services
{
    public interface IFolderPicker
    {
        Task<string?> PickFolderAsync(string title);
    }
}
