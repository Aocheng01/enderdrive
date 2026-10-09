using System;
using System.Globalization;

namespace EnderDrive.ViewModels
{
    /// <summary>Formatos de texto que comparten varias pantallas.</summary>
    public static class Formatters
    {
        private static readonly string[] ShortMonths =
            ["Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic"];

        /// <summary>"1.42 GB", "840 MB"… Con punto decimal siempre, como en el diseño.</summary>
        public static string Size(long bytes) => bytes switch
        {
            >= 1L << 30 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)(1L << 30):0.00} GB"),
            >= 1L << 20 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)(1L << 20):0} MB"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0} KB"),
        };

        /// <summary>"Hoy, 18:30 (Hace 2h)", "Ayer, 22:15", "Hace 3 días" o "12 Ene 2025".</summary>
        public static string RelativeDate(DateTime date) => RelativeDate(date, DateTime.Now);

        public static string RelativeDate(DateTime date, DateTime now)
        {
            var ago = now - date;

            if (date.Date == now.Date)
            {
                var relative = ago.TotalMinutes < 1 ? "ahora mismo"
                    : ago.TotalHours < 1 ? $"Hace {(int)ago.TotalMinutes} min"
                    : $"Hace {(int)ago.TotalHours}h";
                return $"Hoy, {date:HH:mm} ({relative})";
            }

            if (date.Date == now.Date.AddDays(-1))
                return $"Ayer, {date:HH:mm}";

            if (ago.TotalDays < 7)
                return $"Hace {(int)Math.Ceiling(ago.TotalDays)} días";

            return $"{date.Day} {ShortMonths[date.Month - 1]} {date.Year}";
        }

        /// <summary>"12 Ene 2025, 18:30"</summary>
        public static string FullDate(DateTime date) => $"{date.Day} {ShortMonths[date.Month - 1]} {date.Year}, {date:HH:mm}";

        /// <summary>".minecraft/saves": las dos últimas carpetas de una ruta.</summary>
        public static string ShortPath(string path)
        {
            var parts = path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 ? $"{parts[^2]}/{parts[^1]}" : path;
        }
    }
}
