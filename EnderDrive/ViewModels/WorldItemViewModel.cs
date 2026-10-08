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
        public string FolderName { get; }
        public string FolderPath { get; }
        public string LastPlayedText { get; }
        public string SizeText { get; }
        public Bitmap? Icon { get; }
        public bool HasIcon => Icon is not null;

        // Etiquetas de la tarjeta (null = no se muestra)
        public string? VersionText { get; }
        public string? GameModeText { get; }
        public bool IsHardcore { get; }

        public WorldItemViewModel(WorldInfo world)
        {
            Name = world.Name;
            FolderName = world.FolderName;
            FolderPath = world.FolderPath;
            LastPlayedText = world.LastPlayed.ToString("dd/MM/yyyy HH:mm");
            SizeText = FormatSize(world.SizeBytes);
            Icon = LoadIcon(world.IconPath);
            VersionText = world.GameVersion;
            GameModeText = FormatGameMode(world.GameMode);
            IsHardcore = world.IsHardcore;
        }

        private static string FormatSize(long bytes) => bytes switch
        {
            >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
            >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
            _ => $"{bytes / 1024.0:0} KB",
        };

        private static string? FormatGameMode(GameMode? mode) => mode switch
        {
            GameMode.Survival => "Supervivencia",
            GameMode.Creative => "Creativo",
            GameMode.Adventure => "Aventura",
            GameMode.Spectator => "Espectador",
            _ => null,
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
