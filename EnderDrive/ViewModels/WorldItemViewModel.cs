using Avalonia.Media.Imaging;
using EnderDrive.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace EnderDrive.ViewModels
{
    public sealed class WorldItemViewModel
    {
        public string Name { get; }
        public string LastPlayedText { get; }
        public string SizeText { get; }
        public Bitmap? Icon { get; }
        public bool HasIcon => Icon is not null;

        public WorldItemViewModel(WorldInfo world)
        {
            Name = world.Name;
            LastPlayedText = world.LastPlayed.ToString("dd/MM/yyyy HH:mm");
            SizeText = FormatSize(world.SizeBytes);
            Icon = LoadIcon(world.IconPath);
        }

        private static string FormatSize(long bytes) => bytes switch
        {
            >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
            >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
            _ => $"{bytes / 1024.0:0} KB",
        };

        private static Bitmap? LoadIcon(string? path)
        {
            if (path is null)
                return null;

            try
            {
                return new Bitmap(path);
            }
            catch (Exception)
            {
                return null; // icon.png dañado: se mostrará el icono por defecto
            }
        }
    }
}
