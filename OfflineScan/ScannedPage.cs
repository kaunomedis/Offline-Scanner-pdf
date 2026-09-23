using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;

namespace OfflineScan
{
    /// <summary>Vienas nuskenuotas puslapis buferyje (atmintyje).</summary>
    public sealed class ScannedPage : IDisposable
    {
        public Bitmap Image { get; }
        public float Dpi { get; }

        private ScannedPage(Bitmap image, float dpi)
        {
            Image = image;
            Dpi = dpi >= 50 ? dpi : 200;
        }

        /// <summary>Nukopijuoja paveikslėlį į 24 bitų RGB formatą (patogu PDF'ui).</summary>
        public static ScannedPage FromImage(Image src, float dpi)
        {
            var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.DrawImage(src, 0, 0, src.Width, src.Height);
            }
            var page = new ScannedPage(bmp, dpi);
            bmp.SetResolution(page.Dpi, page.Dpi);
            return page;
        }

        /// <summary>Įkelia failą (JPG/PNG/BMP/TIFF, įskaitant daugiapuslapius TIFF).</summary>
        public static List<ScannedPage> FromFile(string path)
        {
            var result = new List<ScannedPage>();
            using var src = System.Drawing.Image.FromFile(path);
            int frames = 1;
            try { frames = src.GetFrameCount(FrameDimension.Page); } catch { }
            for (int i = 0; i < frames; i++)
            {
                if (frames > 1) src.SelectActiveFrame(FrameDimension.Page, i);
                result.Add(FromImage(src, src.HorizontalResolution));
            }
            return result;
        }

        public void Dispose() => Image.Dispose();
    }
}
