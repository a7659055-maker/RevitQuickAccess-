using System;
using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RevitQuickAccess.Quick
{
    /// <summary>
    /// Persists quick-command icons captured from the ribbon as PNG files next to the plugin,
    /// so they survive across sessions and are saved "in the config" together with the command.
    /// </summary>
    public static class IconStore
    {
        private static string Dir
        {
            get
            {
                string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
                string d = Path.Combine(baseDir, "icons");
                try { Directory.CreateDirectory(d); } catch { }
                return d;
            }
        }

        /// <summary>Save a ribbon ImageSource to a PNG; returns the file name (or "" on failure).</summary>
        public static string Save(ImageSource img)
        {
            try
            {
                if (img is not BitmapSource bmp) return "";
                string file = "ic_" + Guid.NewGuid().ToString("N").Substring(0, 12) + ".png";
                string full = Path.Combine(Dir, file);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                using (var fs = new FileStream(full, FileMode.Create, FileAccess.Write))
                    encoder.Save(fs);
                return file;
            }
            catch { return ""; }
        }

        /// <summary>Load a previously saved icon by file name (null if missing/unreadable).</summary>
        public static ImageSource Load(string file)
        {
            if (string.IsNullOrWhiteSpace(file)) return null;
            try
            {
                string full = Path.Combine(Dir, file);
                if (!File.Exists(full)) return null;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;      // don't lock the file
                bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bmp.UriSource = new Uri(full, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }
    }
}
