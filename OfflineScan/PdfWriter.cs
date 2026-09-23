using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;

namespace OfflineScan
{
    /// <summary>
    /// Minimalus PDF rašytojas: kiekvienas puslapis yra JPEG paveikslėlis (DCTDecode).
    /// Jokių išorinių bibliotekų, visas kodas matomas čia.
    /// </summary>
    public static class PdfWriter
    {
        public static void Save(string path, IList<ScannedPage> pages, long jpegQuality = 85)
        {
            if (pages.Count == 0) throw new InvalidOperationException("Nėra puslapių.");

            // Objektai: 1 = Catalog, 2 = Pages, kiekvienam puslapiui: Page, Contents, Image
            int n = pages.Count;
            int total = 2 + 3 * n;
            var offsets = new long[total + 1];

            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            void W(string s) { var b = System.Text.Encoding.ASCII.GetBytes(s); fs.Write(b, 0, b.Length); }
            static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

            W("%PDF-1.4\n");
            fs.Write(new byte[] { 0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A }); // dvejetainio failo žymė

            offsets[1] = fs.Position;
            W("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

            offsets[2] = fs.Position;
            string kids = string.Join(" ", Enumerable.Range(0, n).Select(i => $"{3 + 3 * i} 0 R"));
            W($"2 0 obj\n<< /Type /Pages /Kids [{kids}] /Count {n} >>\nendobj\n");

            for (int i = 0; i < n; i++)
            {
                var page = pages[i];
                int pageObj = 3 + 3 * i, contentObj = pageObj + 1, imageObj = pageObj + 2;
                int wPx = page.Image.Width, hPx = page.Image.Height;
                double wPt = wPx * 72.0 / page.Dpi, hPt = hPx * 72.0 / page.Dpi; // tikras fizinis dydis

                offsets[pageObj] = fs.Position;
                W($"{pageObj} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {F(wPt)} {F(hPt)}] " +
                  $"/Resources << /XObject << /Im0 {imageObj} 0 R >> >> /Contents {contentObj} 0 R >>\nendobj\n");

                string content = $"q {F(wPt)} 0 0 {F(hPt)} 0 0 cm /Im0 Do Q";
                offsets[contentObj] = fs.Position;
                W($"{contentObj} 0 obj\n<< /Length {content.Length} >>\nstream\n{content}\nendstream\nendobj\n");

                byte[] jpeg = EncodeJpeg(page.Image, jpegQuality);
                offsets[imageObj] = fs.Position;
                W($"{imageObj} 0 obj\n<< /Type /XObject /Subtype /Image /Width {wPx} /Height {hPx} " +
                  $"/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpeg.Length} >>\nstream\n");
                fs.Write(jpeg, 0, jpeg.Length);
                W("\nendstream\nendobj\n");
            }

            long xref = fs.Position;
            var sb = new System.Text.StringBuilder();
            sb.Append($"xref\n0 {total + 1}\n0000000000 65535 f \n");
            for (int i = 1; i <= total; i++) sb.Append($"{offsets[i]:D10} 00000 n \n");
            sb.Append($"trailer\n<< /Size {total + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            W(sb.ToString());
        }

        private static byte[] EncodeJpeg(Bitmap bmp, long quality)
        {
            var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            using var prms = new EncoderParameters(1);
            System.Drawing.Imaging.Encoder qualityEncoder = System.Drawing.Imaging.Encoder.Quality;
            prms.Param[0] = new EncoderParameter(qualityEncoder, new long[] { quality });
            using var ms = new MemoryStream();
            bmp.Save(ms, codec, prms);
            return ms.ToArray();
        }
    }
}
