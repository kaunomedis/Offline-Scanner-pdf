using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace OfflineScan
{
    /// <summary>
    /// Kelių lapų skenavimas iš tiektuvo (ADF) per WIA 2.0 sąsają – tą pačią, kurią naudoja
    /// „Windows faksas ir skenavimas“. Visi lapai perduodami VIENU darbu: tvarkyklė kiekvienam
    /// lapui prašo naujo srauto (GetNextStream), todėl lapai nebeprarandami tarp darbų.
    ///
    /// Viskas vyksta atskiroje STA gijoje, kad pagrindinis langas neužšaltų.
    /// Jei WIA 2.0 paruošti nepavyksta (nėra tiektuvo elemento, tvarkyklė nepalaiko ir pan.),
    /// grąžinama null, ir kviečiantysis kodas naudoja senąjį būdą.
    /// </summary>
    public static class WiaAdf2
    {
        // ---------- GUID ----------
        private static readonly Guid ClsidWiaDevMgr2 = new("B6C292BC-7C88-41EE-8B54-8EC92617E599");
        private static readonly Guid CategoryRoot = new("F193526F-59B8-4A26-9888-E16E4F97CE10");
        private static readonly Guid CategoryFlatbed = new("FB607B1F-43F3-488B-855B-FB703EC342A6");
        private static readonly Guid CategoryFeeder = new("FE131934-F84C-42AD-8DA4-6129CDDD7288");
        private static readonly Guid CategoryFeederFront = new("4823175C-3B28-487B-A7E6-EEBC17614FD1");
        private static readonly Guid CategoryFeederBack = new("61CA74D4-39DB-42AA-89B1-8C19C9CD4C23");
        private static readonly Guid CategoryAuto = new("DEFE5FD8-6C97-4DDE-B11E-CB509B270E11");
        private static readonly Guid FormatBmp = new("B96B3CAB-0728-11D3-9D7B-0000F81EF32E");
        private static readonly Guid FormatPng = new("B96B3CAF-0728-11D3-9D7B-0000F81EF32E");

        // ---------- savybės ----------
        private const int WIA_DPS_HORIZONTAL_SHEET_FEED_SIZE = 3076;   // 1/1000 colio
        private const int WIA_DPS_VERTICAL_SHEET_FEED_SIZE = 3077;
        private const int WIA_DPS_DOCUMENT_HANDLING_STATUS = 3087;
        private const int WIA_IPS_DOCUMENT_HANDLING_SELECT = 3088;
        private const int WIA_IPS_PAGES = 3096;                        // 0 = visi lapai
        private const int WIA_IPA_ITEM_NAME = 4098;
        private const int WIA_IPA_FULL_ITEM_NAME = 4099;
        private const int WIA_IPA_DATATYPE = 4103;
        private const int WIA_IPA_DEPTH = 4104;
        private const int WIA_IPA_FORMAT = 4106;
        private const int WIA_IPA_COMPRESSION = 4107;
        private const int WIA_IPS_XRES = 6147;
        private const int WIA_IPS_YRES = 6148;
        private const int WIA_IPS_XPOS = 6149;
        private const int WIA_IPS_YPOS = 6150;
        private const int WIA_IPS_XEXTENT = 6151;
        private const int WIA_IPS_YEXTENT = 6152;
        private const int WIA_IPS_MAX_HORIZONTAL_SIZE = 6165;          // 1/1000 colio
        private const int WIA_IPS_MAX_VERTICAL_SIZE = 6166;

        private static readonly int[] LogPropIds =
        {
            WIA_IPA_ITEM_NAME, WIA_IPA_FULL_ITEM_NAME, WIA_IPS_DOCUMENT_HANDLING_SELECT, WIA_IPS_PAGES,
            WIA_IPA_DATATYPE, WIA_IPA_DEPTH, WIA_IPA_FORMAT, WIA_IPA_COMPRESSION,
            WIA_IPS_XRES, WIA_IPS_YRES, WIA_IPS_XPOS, WIA_IPS_YPOS, WIA_IPS_XEXTENT, WIA_IPS_YEXTENT,
            WIA_IPS_MAX_HORIZONTAL_SIZE, WIA_IPS_MAX_VERTICAL_SIZE
        };

        private const int FRONT_ONLY = 0x20;
        private const int WIA_COMPRESSION_NONE = 0;
        private const int WIA_COMPRESSION_PNG = 8;

        private const int S_OK = 0;
        private const int S_FALSE = 1;
        private const int E_NOTIMPL = unchecked((int)0x80004001);
        private const int E_NOINTERFACE = unchecked((int)0x80004002);
        private const int E_INVALIDARG = unchecked((int)0x80070057);
        private const int WIA_ERROR_PAPER_EMPTY = unchecked((int)0x80210003);

        private const ushort VT_EMPTY = 0, VT_I2 = 2, VT_I4 = 3, VT_BSTR = 8, VT_UI2 = 18, VT_UI4 = 19,
                             VT_INT = 22, VT_UINT = 23, VT_LPWSTR = 31, VT_CLSID = 72;

        /// <summary>
        /// Skenuoja visus lapus iš tiektuvo. Grąžina null, jei WIA 2.0 šiam skeneriui paruošti nepavyko
        /// (tada reikia naudoti senąjį būdą). Klaidos, įvykusios jau pradėjus skenuoti, išmetamos.
        /// </summary>
        public static ScanResult? TryScanFeeder(string deviceId, int dpi, ColorMode mode, SizeF? pageInches,
                                                bool allowPng, Action<string>? status)
        {
            var box = new StatusBox();
            ScanResult? result = null;
            Exception? error = null;

            var thread = new Thread(() =>
            {
                try { result = Core(deviceId, dpi, mode, pageInches, allowPng, box); }
                catch (Exception ex) { error = ex; }
            })
            { IsBackground = true, Name = "WIA2 ADF" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            // Laukiame, bet langas lieka gyvas ir rodo eigą
            string? shown = null;
            while (!thread.Join(50))
            {
                string? s = box.Text;
                if (s != null && s != shown) { shown = s; status?.Invoke(s); }
                System.Windows.Forms.Application.DoEvents();
            }

            if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
            return result;
        }

        private static ScanResult? Core(string deviceId, int dpi, ColorMode mode, SizeF? pageInches, bool allowPng, StatusBox box)
        {
            ScanLog.Section("ADF PER WIA 2.0");
            ScanLog.Write($"Parametrai: DPI={dpi}, režimas={mode}, PNG leidžiamas={(allowPng ? "taip" : "ne")}, ID={deviceId}");

            var keep = new List<object>();   // COM objektai, kuriuos reikia atleisti
            string stage = "WIA 2.0 tvarkyklės kūrimas";
            bool downloadStarted = false;
            try
            {
                Type? type = Type.GetTypeFromCLSID(ClsidWiaDevMgr2);
                if (type == null) { ScanLog.Write("WIA 2.0 tvarkyklė (WiaDevMgr2) nerasta"); return null; }
                var mgr = (IWiaDevMgr2)Activator.CreateInstance(type)!;
                keep.Add(mgr);

                stage = "prisijungimas (CreateDevice)";
                var sw = Stopwatch.StartNew();
                int hr = mgr.CreateDevice(0, deviceId, out IWiaItem2 root);
                if (hr != S_OK || root == null)
                {
                    ScanLog.Write($"CreateDevice nepavyko: 0x{hr:X8}");
                    return null;
                }
                keep.Add(root);
                ScanLog.Write($"Prisijungta per {sw.ElapsedMilliseconds} ms");

                stage = "tiektuvo elemento paieška";
                IWiaItem2? feeder = FindFeeder(root, keep);
                if (feeder == null)
                {
                    ScanLog.Write("Tiektuvo elemento nerasta");
                    return null;
                }

                var fps = (IWiaPropertyStorage)feeder;
                var rps = (IWiaPropertyStorage)root;
                LogProps("Tiektuvas PRIEŠ nustatymus", fps);
                object? status0 = Read(rps, WIA_DPS_DOCUMENT_HANDLING_STATUS);
                ScanLog.Write($"Tiektuvo būsena (šakninis elementas): {Fmt(status0)}");

                // ---------- nustatymai ----------
                stage = "nustatymai";
                WriteInt(fps, WIA_IPS_DOCUMENT_HANDLING_SELECT, FRONT_ONLY, "šaltinis = tik priekinė pusė");
                WriteInt(fps, WIA_IPS_PAGES, 0, "lapų skaičius = visi");

                (int dataType, int depth) = mode switch
                {
                    ColorMode.Color => (3, 24),
                    ColorMode.Gray => (2, 8),
                    _ => (0, 1)
                };
                WriteInt(fps, WIA_IPA_DATATYPE, dataType, "duomenų tipas");
                WriteInt(fps, WIA_IPA_DEPTH, depth, "bitų gylis");

                WriteInt(fps, WIA_IPS_XRES, dpi, "DPI X");
                WriteInt(fps, WIA_IPS_YRES, dpi, "DPI Y");
                int? confirmedDpi = ToInt(Read(fps, WIA_IPS_XRES));
                bool dpiKnown = confirmedDpi.HasValue && confirmedDpi.Value > 0;
                int actualDpi = dpiKnown ? confirmedDpi!.Value : dpi;
                if (!dpiKnown)
                    ScanLog.Write("DĖMESIO: tikrojo DPI perskaityti nepavyko, jis bus įvertintas pagal vaizdo plotį");

                var result = new ScanResult { ActualDpi = actualDpi };
                if (dpiKnown && actualDpi != dpi)
                {
                    ScanLog.Write($"DĖMESIO: tvarkyklė paliko {actualDpi} DPI vietoj {dpi}");
                    result.Warnings.Add($"Skeneris nepalaiko {dpi} DPI, todėl nuskenuota {actualDpi} DPI.");
                }

                // Plotas pagal tikrąjį DPI
                double maxW = (ToInt(Read(fps, WIA_IPS_MAX_HORIZONTAL_SIZE)) ?? ToInt(Read(rps, WIA_DPS_HORIZONTAL_SHEET_FEED_SIZE)) ?? 0) / 1000.0;
                double maxH = (ToInt(Read(fps, WIA_IPS_MAX_VERTICAL_SIZE)) ?? ToInt(Read(rps, WIA_DPS_VERTICAL_SHEET_FEED_SIZE)) ?? 0) / 1000.0;
                double wIn = pageInches?.Width ?? (maxW > 0 ? maxW : 8.27);
                double hIn = pageInches?.Height ?? (maxH > 0 ? maxH : 11.69);
                if (maxW > 0) wIn = Math.Min(wIn, maxW);
                if (maxH > 0) hIn = Math.Min(hIn, maxH);
                int extW = (int)(wIn * actualDpi), extH = (int)(hIn * actualDpi);
                ScanLog.Write($"Plotas: tiektuvo maksimumas {maxW:0.###} x {maxH:0.###} in; naudojama {wIn:0.###} x {hIn:0.###} in → {extW} x {extH} px prie {actualDpi} DPI");
                WriteInt(fps, WIA_IPS_XPOS, 0, "pradžia X");
                WriteInt(fps, WIA_IPS_YPOS, 0, "pradžia Y");
                WriteInt(fps, WIA_IPS_XEXTENT, extW, "plotis");
                WriteInt(fps, WIA_IPS_YEXTENT, extH, "aukštis");

                bool usePng = allowPng;
                ApplyFormat(fps, usePng);
                LogProps("Tiektuvas PO nustatymų", fps);

                // ---------- perdavimas ----------
                var transfer = (IWiaTransfer)feeder;
                for (int attempt = 1; attempt <= 2; attempt++)
                {
                    stage = "perdavimas (Download)";
                    var cb = new WiaTransferCallback(box);
                    ScanLog.Write($"Download pradėtas, formatas {(usePng ? "PNG" : "BMP")}");
                    box.Text = "Skenuojama iš tiektuvo…";
                    downloadStarted = true;
                    sw.Restart();
                    int dhr = transfer.Download(0, cb);
                    long ms = sw.ElapsedMilliseconds;
                    int effective = dhr < 0 ? dhr : cb.DeviceError;
                    ScanLog.Write($"Download baigtas per {ms} ms: hr=0x{dhr:X8}, srautų: {cb.Streams.Count}" +
                                  (cb.DeviceError != 0 ? $", įrenginio klaida 0x{cb.DeviceError:X8}" : ""));

                    stage = "vaizdų apdorojimas";
                    ConvertStreams(cb, actualDpi, dpiKnown, wIn, result);
                    int pages = result.Pages.Count;

                    if (effective == S_OK)
                    {
                        if (pages == 0)
                            throw new InvalidOperationException("Tiektuvas negrąžino nė vieno lapo. Patikrinkite, ar įdėtas popierius.");
                        ScanLog.Write($"WIA 2.0 ADF baigta sėkmingai, lapų: {pages}");
                        return result;
                    }
                    if (effective == WIA_ERROR_PAPER_EMPTY)
                    {
                        if (pages == 0) throw new InvalidOperationException("Tiektuve (ADF) nėra lapų.");
                        ScanLog.Write($"Tiektuvas ištuštėjo, lapų: {pages}");
                        return result;
                    }
                    if (pages > 0)
                    {
                        string why = WiaScanner.Describe(new COMException("", effective));
                        ScanLog.Write($"Darbas baigtas su klaida 0x{effective:X8}, bet išsaugoma lapų: {pages}");
                        result.Warnings.Add($"Nuskenuota lapų: {pages}. Skeneris darbą užbaigė su klaida: {why} Patikrinkite, ar nuskenuoti visi lapai.");
                        return result;
                    }

                    // Nė vieno lapo
                    if (usePng && attempt == 1 && ms < 3000)
                    {
                        ScanLog.Write($"PNG atmestas per {ms} ms (0x{effective:X8}), bandoma BMP");
                        usePng = false;
                        ApplyFormat(fps, usePng);
                        continue;
                    }
                    if (ms < 3000 && (effective == E_INVALIDARG || effective == E_NOTIMPL || effective == E_NOINTERFACE))
                    {
                        ScanLog.Write($"WIA 2.0 perdavimą tvarkyklė atmetė iškart (0x{effective:X8}), grįžtama prie senojo būdo");
                        return null;
                    }
                    throw new COMException("WIA 2.0 perdavimas iš tiektuvo nepavyko.", effective);
                }
                return null;
            }
            catch (Exception ex) when (!downloadStarted)
            {
                ScanLog.Write($"WIA 2.0 paruošimas nepavyko etape „{stage}“: 0x{ScanLog.HR(ex):X8} {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                ScanLog.Write($"KLAIDA WIA 2.0 etape „{stage}“: 0x{ScanLog.HR(ex):X8} {ex.Message}");
                throw;
            }
            finally
            {
                for (int i = keep.Count - 1; i >= 0; i--)
                {
                    try { Marshal.FinalReleaseComObject(keep[i]); } catch { }
                }
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        // ---------- elementų paieška ----------

        private static IWiaItem2? FindFeeder(IWiaItem2 root, List<object> keep)
        {
            int hr = root.EnumChildItems(IntPtr.Zero, out IEnumWiaItem2 en);
            if (hr != S_OK || en == null)
            {
                ScanLog.Write($"EnumChildItems nepavyko: 0x{hr:X8}");
                return null;
            }
            keep.Add(en);

            IWiaItem2? byCategory = null, byName = null;
            ScanLog.Write("WIA 2.0 elementai:");
            while (en.Next(1, out IWiaItem2 child, out uint fetched) == S_OK && fetched == 1 && child != null)
            {
                keep.Add(child);
                child.GetItemCategory(out Guid cat);
                child.GetItemType(out int itemType);
                var ps = (IWiaPropertyStorage)child;
                string name = Read(ps, WIA_IPA_ITEM_NAME) as string ?? "?";
                string full = Read(ps, WIA_IPA_FULL_ITEM_NAME) as string ?? "?";
                ScanLog.Write($"  {name} ({full}): kategorija {CategoryName(cat)} {{{cat}}}, tipas 0x{itemType:X}");

                if (byCategory == null && cat == CategoryFeeder) byCategory = child;
                if (byName == null && (name.IndexOf("Feeder", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       name.IndexOf("ADF", StringComparison.OrdinalIgnoreCase) >= 0))
                    byName = child;
            }

            IWiaItem2? chosen = byCategory ?? byName;
            if (chosen != null)
                ScanLog.Write($"Pasirinktas tiektuvo elementas: {Read((IWiaPropertyStorage)chosen, WIA_IPA_FULL_ITEM_NAME)}" +
                              (byCategory != null ? " (pagal kategoriją)" : " (pagal pavadinimą)"));
            return chosen;
        }

        private static string CategoryName(Guid g) =>
            g == CategoryFeeder ? "FEEDER" :
            g == CategoryFlatbed ? "FLATBED" :
            g == CategoryFeederFront ? "FEEDER_FRONT" :
            g == CategoryFeederBack ? "FEEDER_BACK" :
            g == CategoryAuto ? "AUTO" :
            g == CategoryRoot ? "ROOT" : "nežinoma";

        // ---------- vaizdai ----------

        private static void ConvertStreams(WiaTransferCallback cb, int dpi, bool dpiKnown, double widthIn, ScanResult result)
        {
            for (int i = 0; i < cb.Streams.Count; i++)
            {
                byte[] data;
                try { data = ReadAll(cb.Streams[i]); }
                finally { try { Marshal.FinalReleaseComObject(cb.Streams[i]); } catch { } }

                if (data.Length == 0)
                {
                    ScanLog.Write($"  Srautas #{i + 1}: tuščias, praleidžiamas");
                    continue;
                }
                try
                {
                    byte[] image = EnsureBmpHeader(data);
                    using var ms = new MemoryStream(image);
                    using var bmp = new Bitmap(ms);
                    int pageDpi = dpiKnown ? dpi : EstimateDpi(bmp.Width, widthIn, dpi);
                    result.Pages.Add(ScannedPage.FromImage(bmp, pageDpi));
                    ScanLog.Write($"  Lapas {result.Pages.Count}: {bmp.Width} x {bmp.Height} px, {data.Length / 1024.0:N0} KB, " +
                                  $"antraštėje {bmp.HorizontalResolution:0} DPI, PDF'ui naudojamas {pageDpi} DPI" +
                                  (dpiKnown ? "" : " (įvertinta pagal plotį)"));
                }
                catch (Exception ex)
                {
                    int n = Math.Min(16, data.Length);
                    ScanLog.Write($"  Srautas #{i + 1}: vaizdo atpažinti nepavyko ({ex.Message}); {data.Length} B, pradžia: {BitConverter.ToString(data, 0, n)}");
                    result.Warnings.Add("Vieno lapo vaizdo nepavyko atpažinti (žr. žurnalą).");
                }
            }
        }

        /// <summary>DPI pagal vaizdo plotį ir numatomą lapo plotį, suapvalinta iki standartinės reikšmės.</summary>
        private static int EstimateDpi(int widthPx, double widthIn, int fallback)
        {
            if (widthPx <= 0 || widthIn <= 0) return fallback;
            double estimate = widthPx / widthIn;
            int[] standard = { 75, 100, 150, 200, 240, 300, 400, 600, 1200 };
            int best = fallback;
            double bestDiff = double.MaxValue;
            foreach (int v in standard)
            {
                double d = Math.Abs(v - estimate);
                if (d < bestDiff) { bestDiff = d; best = v; }
            }
            return best;
        }

        private static byte[] ReadAll(ComTypes.IStream s)
        {
            s.Stat(out ComTypes.STATSTG st, 1); // STATFLAG_NONAME
            long size = st.cbSize;
            if (size <= 0 || size > int.MaxValue) return Array.Empty<byte>();
            s.Seek(0, 0, IntPtr.Zero);
            var buf = new byte[size];
            s.Read(buf, (int)size, IntPtr.Zero);
            return buf;
        }

        /// <summary>Kai kurios tvarkyklės BMP atiduoda be failo antraštės (tik DIB). Tada ją pridedame.</summary>
        private static byte[] EnsureBmpHeader(byte[] d)
        {
            if (d.Length >= 2 && d[0] == (byte)'B' && d[1] == (byte)'M') return d;
            if (d.Length < 40) return d;
            int biSize = BitConverter.ToInt32(d, 0);
            if (biSize != 40 && biSize != 108 && biSize != 124) return d; // ne DIB (pvz., PNG)
            short bitCount = BitConverter.ToInt16(d, 14);
            int clrUsed = BitConverter.ToInt32(d, 32);
            int palette = bitCount <= 8 ? (clrUsed > 0 ? clrUsed : 1 << bitCount) : clrUsed;
            int offBits = 14 + biSize + palette * 4;
            var r = new byte[d.Length + 14];
            r[0] = (byte)'B';
            r[1] = (byte)'M';
            BitConverter.GetBytes(r.Length).CopyTo(r, 2);
            BitConverter.GetBytes(offBits).CopyTo(r, 10);
            Buffer.BlockCopy(d, 0, r, 14, d.Length);
            ScanLog.Write("  (BMP be failo antraštės – antraštė pridėta)");
            return r;
        }

        // ---------- savybės ----------

        private static void ApplyFormat(IWiaPropertyStorage ps, bool png)
        {
            WriteInt(ps, WIA_IPA_COMPRESSION, png ? WIA_COMPRESSION_PNG : WIA_COMPRESSION_NONE, "suspaudimas");
            WriteGuid(ps, WIA_IPA_FORMAT, png ? FormatPng : FormatBmp, "formatas");
        }

        private static void LogProps(string title, IWiaPropertyStorage ps)
        {
            var lines = new List<string> { title + ":" };
            foreach (int id in LogPropIds)
            {
                object? v = Read(ps, id);
                if (v != null) lines.Add($"    [{id}] = {Fmt(v)}");
            }
            ScanLog.Write(string.Join("\n", lines));
        }

        private static string Fmt(object? v) => v switch
        {
            null => "nėra",
            string s => $"\"{s}\"",
            Guid g when g == FormatBmp => "BMP",
            Guid g when g == FormatPng => "PNG",
            Guid g => "{" + g + "}",
            _ => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? "?"
        };

        private static int? ToInt(object? v) => v is int i ? i : null;

        private static object? Read(IWiaPropertyStorage ps, int id)
        {
            var spec = new[] { new PROPSPEC { ulKind = 1, id = (IntPtr)id } };
            var pv = new PROPVARIANT[1];
            int hr;
            try { hr = ps.ReadMultiple(1, spec, pv); }
            catch { return null; }
            if (hr != S_OK && hr != S_FALSE) return null;
            try
            {
                long raw = pv[0].p1.ToInt64();
                return pv[0].vt switch
                {
                    VT_EMPTY => null,
                    VT_I2 => (int)(short)raw,
                    VT_UI2 => (int)(ushort)raw,
                    VT_I4 or VT_INT => unchecked((int)raw),
                    VT_UI4 or VT_UINT => unchecked((int)(uint)raw),
                    VT_BSTR => pv[0].p1 == IntPtr.Zero ? "" : Marshal.PtrToStringBSTR(pv[0].p1),
                    VT_LPWSTR => Marshal.PtrToStringUni(pv[0].p1),
                    VT_CLSID => pv[0].p1 == IntPtr.Zero ? null : Marshal.PtrToStructure<Guid>(pv[0].p1),
                    _ => $"(VT {pv[0].vt})"
                };
            }
            finally { PropVariantClear(ref pv[0]); }
        }

        private static bool WriteInt(IWiaPropertyStorage ps, int id, int value, string label)
        {
            var spec = new[] { new PROPSPEC { ulKind = 1, id = (IntPtr)id } };
            var pv = new[] { new PROPVARIANT { vt = VT_I4, p1 = (IntPtr)value } };
            int hr;
            try { hr = ps.WriteMultiple(1, spec, pv, 2); }
            catch (Exception ex) { hr = ScanLog.HR(ex); }
            ScanLog.Write($"WIA2 SET [{id}] {label} = {value}: {(hr == S_OK ? "priimta" : $"ATMESTA 0x{hr:X8}")}, dabar {Fmt(Read(ps, id))}");
            return hr == S_OK;
        }

        private static bool WriteGuid(IWiaPropertyStorage ps, int id, Guid value, string label)
        {
            IntPtr mem = Marshal.AllocCoTaskMem(16);
            int hr;
            try
            {
                Marshal.StructureToPtr(value, mem, false);
                var spec = new[] { new PROPSPEC { ulKind = 1, id = (IntPtr)id } };
                var pv = new[] { new PROPVARIANT { vt = VT_CLSID, p1 = mem } };
                try { hr = ps.WriteMultiple(1, spec, pv, 2); }
                catch (Exception ex) { hr = ScanLog.HR(ex); }
            }
            finally { Marshal.FreeCoTaskMem(mem); }
            ScanLog.Write($"WIA2 SET [{id}] {label} = {Fmt(value)}: {(hr == S_OK ? "priimta" : $"ATMESTA 0x{hr:X8}")}, dabar {Fmt(Read(ps, id))}");
            return hr == S_OK;
        }

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PROPVARIANT pvar);

        [DllImport("ole32.dll")]
        internal static extern int CreateStreamOnHGlobal(IntPtr hGlobal, bool fDeleteOnRelease, out ComTypes.IStream ppstm);

        internal sealed class StatusBox
        {
            private volatile string? _text;
            public string? Text { get => _text; set => _text = value; }
        }
    }

    // =====================================================================
    // Perdavimo atgalinis iškvietimas: tvarkyklė jį kviečia kiekvienam lapui
    // =====================================================================

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    internal sealed class WiaTransferCallback : IWiaTransferCallback
    {
        private const int MSG_STATUS = 1, MSG_END_OF_STREAM = 2, MSG_END_OF_TRANSFER = 3, MSG_DEVICE_STATUS = 5, MSG_NEW_PAGE = 6;
        private readonly WiaAdf2.StatusBox _box;
        private int _lastStep = -1;

        public List<ComTypes.IStream> Streams { get; } = new();
        public int DeviceError { get; private set; }

        public WiaTransferCallback(WiaAdf2.StatusBox box) => _box = box;

        public int TransferCallback(int lFlags, ref WiaTransferParams p)
        {
            switch (p.lMessage)
            {
                case MSG_STATUS:
                    int step = p.lPercentComplete / 25;
                    if (step != _lastStep)
                    {
                        _lastStep = step;
                        ScanLog.Write($"  WIA2: lapas {Streams.Count}, {p.lPercentComplete}% ({p.ulTransferredBytes / 1024} KB)");
                    }
                    _box.Text = $"Skenuojama iš tiektuvo: {Streams.Count} lapas, {p.lPercentComplete}%";
                    break;
                case MSG_END_OF_STREAM:
                    ScanLog.Write($"  WIA2: lapo {Streams.Count} pabaiga ({p.ulTransferredBytes / 1024} KB), hr=0x{p.hrErrorStatus:X8}");
                    break;
                case MSG_END_OF_TRANSFER:
                    ScanLog.Write($"  WIA2: perdavimo pabaiga, hr=0x{p.hrErrorStatus:X8}");
                    break;
                case MSG_NEW_PAGE:
                    ScanLog.Write($"  WIA2: naujas lapas, hr=0x{p.hrErrorStatus:X8}");
                    break;
                case MSG_DEVICE_STATUS:
                    ScanLog.Write($"  WIA2: įrenginio būsena hr=0x{p.hrErrorStatus:X8}");
                    if (p.hrErrorStatus < 0)
                    {
                        DeviceError = p.hrErrorStatus;
                        return 1; // S_FALSE: nutraukti, kad nebūtų begalinio kartojimo
                    }
                    break;
                default:
                    ScanLog.Write($"  WIA2: pranešimas {p.lMessage}, {p.lPercentComplete}%, hr=0x{p.hrErrorStatus:X8}");
                    break;
            }
            return 0;
        }

        public int GetNextStream(int lFlags, string bstrItemName, string bstrFullItemName, out ComTypes.IStream ppDestination)
        {
            int hr = WiaAdf2.CreateStreamOnHGlobal(IntPtr.Zero, true, out ComTypes.IStream s);
            if (hr != 0)
            {
                ScanLog.Write($"  WIA2: nepavyko sukurti srauto: 0x{hr:X8}");
                ppDestination = null!;
                return hr;
            }
            Streams.Add(s);
            _lastStep = -1;
            ScanLog.Write($"  WIA2: GetNextStream #{Streams.Count}: {bstrItemName} ({bstrFullItemName})");
            _box.Text = $"Skenuojama iš tiektuvo: {Streams.Count} lapas";
            ppDestination = s;
            return 0;
        }
    }

    // =====================================================================
    // WIA 2.0 COM sąsajos (wia_lh.h). Metodų tvarka turi atitikti originalą.
    // =====================================================================

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROPSPEC
    {
        public uint ulKind;   // 1 = PRSPEC_PROPID
        public IntPtr id;     // propid (sąjunga su LPOLESTR)
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROPVARIANT
    {
        public ushort vt;
        public ushort r1, r2, r3;
        public IntPtr p1;
        public IntPtr p2;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WiaTransferParams
    {
        public int lMessage;
        public int lPercentComplete;
        public ulong ulTransferredBytes;
        public int hrErrorStatus;
    }

    [ComImport, Guid("79C07CF1-CBDD-41EE-8EC3-F00080CADA7A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IWiaDevMgr2
    {
        [PreserveSig] int EnumDeviceInfo(int lFlags, out IntPtr ppIEnum);
        [PreserveSig] int CreateDevice(int lFlags, [MarshalAs(UnmanagedType.BStr)] string bstrDeviceID, out IWiaItem2 ppWiaItem2Root);
    }

    [ComImport, Guid("6CBA0075-1287-407D-9B77-CF0E030435CC"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IWiaItem2
    {
        [PreserveSig] int CreateChildItem(int lItemFlags, int lCreationFlags, [MarshalAs(UnmanagedType.BStr)] string bstrItemName, out IWiaItem2 ppIWiaItem2);
        [PreserveSig] int DeleteItem(int lFlags);
        [PreserveSig] int EnumChildItems(IntPtr pCategoryGUID, out IEnumWiaItem2 ppIEnumWiaItem2);
        [PreserveSig] int FindItemByName(int lFlags, [MarshalAs(UnmanagedType.BStr)] string bstrFullItemName, out IWiaItem2 ppIWiaItem2);
        [PreserveSig] int GetItemCategory(out Guid pItemCategoryGUID);
        [PreserveSig] int GetItemType(out int pItemType);
    }

    [ComImport, Guid("59970AF4-CD0D-44D9-AB24-52295630E582"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IEnumWiaItem2
    {
        [PreserveSig] int Next(uint cElt, out IWiaItem2 ppIWiaItem2, out uint pcEltFetched);
    }

    [ComImport, Guid("98B5E8A0-29CC-491A-AAC0-E6DB4FDCCEB6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IWiaPropertyStorage
    {
        // Masyvai būtinai kaip paprasti C masyvai (LPArray). Be to .NET 8 bando juos perduoti kaip
        // SAFEARRAY ir visada grąžina 0x80131165 („Type library is not registered“).
        [PreserveSig] int ReadMultiple(uint cpspec,
            [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] PROPSPEC[] rgpspec,
            [In, Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] PROPVARIANT[] rgpropvar);
        [PreserveSig] int WriteMultiple(uint cpspec,
            [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] PROPSPEC[] rgpspec,
            [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] PROPVARIANT[] rgpropvar,
            uint propidNameFirst);
    }

    [ComImport, Guid("C39D6942-2F4E-4D04-92FE-4EF4D3A1DE5A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IWiaTransfer
    {
        [PreserveSig] int Download(int lFlags, IWiaTransferCallback pIWiaTransferCallback);
    }

    [ComImport, Guid("27D4EAAF-28A6-4CA5-9AAB-E678168B9527"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IWiaTransferCallback
    {
        [PreserveSig] int TransferCallback(int lFlags, ref WiaTransferParams pWiaTransferParams);
        [PreserveSig] int GetNextStream(int lFlags, [MarshalAs(UnmanagedType.BStr)] string bstrItemName,
                                        [MarshalAs(UnmanagedType.BStr)] string bstrFullItemName, out ComTypes.IStream ppDestination);
    }
}
