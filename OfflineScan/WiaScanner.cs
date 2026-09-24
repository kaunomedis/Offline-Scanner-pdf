using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace OfflineScan
{
    public enum ColorMode { Color, Gray, BlackWhite }

    public sealed class ScannerInfo
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public override string ToString() => Name;
    }

    /// <summary>Skenavimo rezultatas: lapai ir pastabos vartotojui.</summary>
    public sealed class ScanResult
    {
        public List<ScannedPage> Pages { get; } = new();
        public List<string> Warnings { get; } = new();
        public int ActualDpi { get; set; }
    }

    /// <summary>Ką skeneris palaiko (nuskaitoma iš tvarkyklės).</summary>
    public sealed class ScannerCaps
    {
        public List<int> Resolutions { get; } = new();
        public List<ColorMode> Modes { get; } = new();
        public bool HasFeeder { get; set; }
        public bool HasFlatbed { get; set; } = true;
        public bool SupportsPng { get; set; }
    }

    /// <summary>
    /// Skenavimas per Windows Image Acquisition (WIA 2.0). WIA yra Windows dalis,
    /// jokio interneto, paskyrų ar gamintojo programų nereikia. Naudojamas vėlyvasis
    /// susiejimas (dynamic), todėl nereikia pridėti COM nuorodų į projektą.
    /// Visi veiksmai rašomi į diagnostinį žurnalą (ScanLog).
    /// </summary>
    public static class WiaScanner
    {
        private const string FormatBmp = "{B96B3CAB-0728-11D3-9D7B-0000F81EF32E}";
        private const string FormatPng = "{B96B3CAF-0728-11D3-9D7B-0000F81EF32E}";
        private const int ScannerDeviceType = 1;

        // WIA savybių ID
        private const int DIP_DEV_NAME = 7;
        private const int DPS_HORIZONTAL_BED_SIZE = 3074;        // 1/1000 colio
        private const int DPS_VERTICAL_BED_SIZE = 3075;
        private const int DPS_HORIZONTAL_SHEET_FEED_SIZE = 3076;
        private const int DPS_VERTICAL_SHEET_FEED_SIZE = 3077;
        private const int DPS_DOCUMENT_HANDLING_CAPABILITIES = 3086;
        private const int DPS_DOCUMENT_HANDLING_STATUS = 3087;
        private const int DPS_DOCUMENT_HANDLING_SELECT = 3088;   // 1 = tiektuvas, 2 = stiklas
        private const int DPS_PAGES = 3096;
        private const int IPA_ITEM_NAME = 4098;
        private const int IPA_DATATYPE = 4103;
        private const int IPA_DEPTH = 4104;
        private const int IPA_FORMAT = 4106;
        private const int IPA_COMPRESSION = 4107;
        private const int WIA_COMPRESSION_PNG = 8;
        private const int IPS_CUR_INTENT = 6146;
        private const int IPS_XRES = 6147;
        private const int IPS_YRES = 6148;
        private const int IPS_XPOS = 6149;
        private const int IPS_YPOS = 6150;
        private const int IPS_XEXTENT = 6151;
        private const int IPS_YEXTENT = 6152;

        /// <summary>Savybės, kurių būsena rašoma į žurnalą prieš ir po nustatymų.</summary>
        private static readonly int[] KeyProps =
        {
            3074, 3075, 3076, 3077,       // stiklo ir tiektuvo dydis
            3086, 3087, 3088, 3096,       // tiektuvo galimybės, būsena, šaltinis, lapų skaičius
            3097, 3098, 3099,             // puslapio dydis, plotis, aukštis (WIA 2.0)
            4103, 4104, 4106, 4107,       // duomenų tipas, bitų gylis, formatas, suspaudimas
            6146, 6147, 6148,             // intent, DPI X, DPI Y
            6149, 6150, 6151, 6152        // plotas: X, Y, plotis, aukštis (px)
        };

        private const int WIA_ERROR_PAPER_EMPTY = unchecked((int)0x80210003);
        private const int WIA_ERROR_BUSY = unchecked((int)0x80210006);
        private const int WIA_ERROR_WARMING_UP = unchecked((int)0x80210007);

        private static dynamic Create(string progId)
        {
            var t = Type.GetTypeFromProgID(progId)
                    ?? throw new InvalidOperationException("WIA komponentas nerastas šiame kompiuteryje.");
            return Activator.CreateInstance(t)!;
        }

        /// <summary>Kartoja veiksmą, kol skeneris nebe užimtas (arba baigiasi laikas).</summary>
        private static T Retry<T>(string label, Func<T> action, int timeoutSeconds = 30)
        {
            var start = DateTime.Now;
            int attempt = 0;
            while (true)
            {
                attempt++;
                try
                {
                    T result = action();
                    if (attempt > 1)
                        ScanLog.Write($"{label}: pavyko po {attempt} bandymų ({(DateTime.Now - start).TotalSeconds:0.0} s)");
                    return result;
                }
                catch (Exception ex) when ((HResult(ex) == WIA_ERROR_BUSY || HResult(ex) == WIA_ERROR_WARMING_UP)
                                           && (DateTime.Now - start).TotalSeconds < timeoutSeconds)
                {
                    ScanLog.Write($"{label}: bandymas {attempt} – {(HResult(ex) == WIA_ERROR_BUSY ? "užimta" : "šyla")} " +
                                  $"(0x{HResult(ex):X8}), laukiama ~1 s");
                    for (int i = 0; i < 5; i++) // laukiam ~1 s, bet langas lieka gyvas
                    {
                        System.Threading.Thread.Sleep(200);
                        System.Windows.Forms.Application.DoEvents();
                    }
                }
            }
        }

        public static List<ScannerInfo> ListScanners()
        {
            ScanLog.Section("SKENERIŲ PAIEŠKA");
            var list = new List<ScannerInfo>();
            dynamic manager = Create("WIA.DeviceManager");
            dynamic infos = manager.DeviceInfos;
            int count = Convert.ToInt32(infos.Count);
            ScanLog.Write($"WIA įrenginių iš viso: {count}");
            for (int i = 1; i <= count; i++)
            {
                dynamic di = infos[i];
                int type = Convert.ToInt32(di.Type);
                string id = Convert.ToString(di.DeviceID) ?? "";
                string name = Convert.ToString(GetProp(di.Properties, DIP_DEV_NAME)) ?? "Skeneris";
                ScanLog.Write($"  #{i}: tipas={type}{(type == ScannerDeviceType ? " (skeneris)" : "")}, \"{name}\", ID={id}");
                if (type != ScannerDeviceType) continue;
                list.Add(new ScannerInfo { Id = id, Name = name });
            }
            return list;
        }

        /// <summary>Pilnas skenerio aprašymas žurnale: visi elementai ir visos jų savybės. Nieko neskenuoja.</summary>
        public static void Diagnose(string deviceId)
        {
            ScanLog.Section("DIAGNOSTIKA");
            dynamic? info = FindDeviceInfo(deviceId);
            if (info == null) throw new InvalidOperationException("Skeneris nerastas. Paspauskite ↻ ir bandykite dar kartą.");
            try
            {
                DumpProps("DeviceInfo savybės", info.Properties);
                dynamic device = Retry<object>("Connect", () => info.Connect());
                DumpProps("Skenerio (šakninio elemento) savybės", device.Properties);
                DumpItems(device, "Items", 0);
                ScanLog.Write("Diagnostika baigta.");
            }
            finally
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        /// <summary>
        /// Atleidžia visus programos laikomus WIA objektus, kad kitas prisijungimas prasidėtų švariai.
        /// Windows WIA paslaugos ir tvarkyklių perkrauti negalime (tam reikia administratoriaus teisių).
        /// </summary>
        public static void ResetSession()
        {
            ScanLog.Section("WIA SESIJOS ATNAUJINIMAS");
            for (int i = 0; i < 2; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            try { System.Runtime.InteropServices.Marshal.CleanupUnusedObjectsInCurrentContext(); } catch { }
            ScanLog.Write("WIA objektai atleisti");
        }

        /// <summary>Nuskaito, kokius DPI, spalvų režimus ir šaltinius palaiko skeneris.</summary>
        public static ScannerCaps GetCapabilities(string deviceId)
        {
            ScanLog.Section("SKENERIO GALIMYBĖS");
            var caps = new ScannerCaps();
            dynamic? info = FindDeviceInfo(deviceId);
            if (info == null) throw new InvalidOperationException("Skeneris nerastas. Paspauskite ↻ ir bandykite dar kartą.");
            string scannerName = Convert.ToString(GetProp(info.Properties, DIP_DEV_NAME)) ?? "?";
            ScanLog.Write($"Skeneris: {scannerName}");
            try
            {
                dynamic device = Retry<object>("Connect", () => info.Connect());
                dynamic item = device.Items[1];

                object? handling = GetProp(device.Properties, DPS_DOCUMENT_HANDLING_CAPABILITIES);
                int h = ToIntOr(handling, -1);
                if (h >= 0)
                {
                    caps.HasFeeder = (h & 1) != 0;
                    caps.HasFlatbed = (h & 2) != 0;
                }
                else
                {
                    caps.HasFeeder = FindProp(device.Properties, DPS_DOCUMENT_HANDLING_SELECT) != null;
                }

                List<int> res = ReadAllowedInts(item.Properties, IPS_XRES, new[] { 75, 100, 150, 200, 240, 300, 400, 600, 1200 });
                caps.Resolutions.AddRange(res);

                List<int> types = ReadAllowedInts(item.Properties, IPA_DATATYPE, new[] { 0, 2, 3 });
                if (types.Count == 0 || types.Contains(3)) caps.Modes.Add(ColorMode.Color);
                if (types.Count == 0 || types.Contains(2)) caps.Modes.Add(ColorMode.Gray);
                if (types.Count == 0 || types.Contains(0)) caps.Modes.Add(ColorMode.BlackWhite);
                caps.SupportsPng = (bool)ListContains(item.Properties, IPA_COMPRESSION, WIA_COMPRESSION_PNG);

                ScanLog.Write($"DPI: {(caps.Resolutions.Count > 0 ? string.Join(", ", caps.Resolutions) : "nežinoma")}; " +
                              $"režimai: {string.Join(", ", caps.Modes)}; " +
                              $"stiklas: {(caps.HasFlatbed ? "taip" : "ne")}; tiektuvas: {(caps.HasFeeder ? "taip" : "ne")}; " +
                              $"suspaustas perdavimas (PNG): {(caps.SupportsPng ? "taip" : "ne")}");
                return caps;
            }
            finally
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        /// <param name="pageInches">Skenavimo plotas coliais; null = visas skenerio plotas.</param>
        /// <param name="feederPng">Ar tiektuvo skenavimui per WIA 2.0 prašyti PNG (skeneris jį palaiko).</param>
        /// <param name="status">Eigos pranešimai būsenos juostai (tik WIA 2.0 tiektuvui).</param>
        public static ScanResult Scan(string deviceId, int dpi, ColorMode mode, bool useFeeder, SizeF? pageInches,
                                      bool feederPng = false, Action<string>? status = null)
        {
            ScanLog.Section("SKENAVIMAS");
            ScanLog.Write($"Parametrai: DPI={dpi}, režimas={mode}, šaltinis={(useFeeder ? "tiektuvas (ADF)" : "stiklas")}, " +
                          $"formatas={(pageInches.HasValue ? $"{pageInches.Value.Width:0.##} x {pageInches.Value.Height:0.##} in" : "visas plotas")}, " +
                          $"ID={deviceId}");

            // Tiektuvas: pirmiausia WIA 2.0 (visi lapai vienu darbu). Jei šiam skeneriui
            // WIA 2.0 paruošti nepavyksta, naudojamas senasis būdas (patikimai tik pirmas lapas).
            if (useFeeder)
            {
                ScanResult? viaWia2 = WiaAdf2.TryScanFeeder(deviceId, dpi, mode, pageInches, feederPng, status);
                if (viaWia2 != null) return viaWia2;
                ScanLog.Section("SKENAVIMAS (SENASIS BŪDAS)");
                ScanLog.Write("WIA 2.0 tiektuvo paruošti nepavyko, naudojamas senasis būdas");
            }

            string stage = "skenerio paieška";
            var result = new ScanResult();
            var pages = result.Pages;
            var sw = Stopwatch.StartNew();
            try
            {
                dynamic? info = FindDeviceInfo(deviceId);
                if (info == null) throw new InvalidOperationException("Skeneris nerastas. Paspauskite ↻ ir bandykite dar kartą.");

                stage = "prisijungimas (Connect)";
                dynamic device = Retry<object>("Connect", () => info.Connect());
                ScanLog.Write($"Prisijungta per {sw.ElapsedMilliseconds} ms");

                stage = "elementų nuskaitymas";
                int itemCount = SafeInt(() => device.Items.Count);
                ScanLog.Write($"Šakninių elementų: {itemCount}, naudojamas Items[1]");
                dynamic item = device.Items[1];
                ScanLog.Write("Items[1]: " + (string)ItemLabel(item));

                LogKeyProps("Skeneris PRIEŠ nustatymus", device.Properties);
                LogKeyProps("Elementas PRIEŠ nustatymus", item.Properties);

                // Šaltinis: tiektuvas (ADF) ar stiklas
                stage = "šaltinio nustatymas";
                SetProp(device.Properties, DPS_DOCUMENT_HANDLING_SELECT, useFeeder ? 1 : 2, "skeneris");
                // Visada aiškiai: 1 lapas per perdavimą.
                // Pages = 0 („visi lapai“) senoji WIA sąsaja atmeta pradedant skenuoti (0x80070057);
                // patikrinta su Lexmark MX410de, HP LaserJet Pro M225dw ir HP LaserJet M426fdn.
                SetProp(device.Properties, DPS_PAGES, 1, "skeneris");

                // Spalvos režimas (tik jei skeneris jį palaiko)
                stage = "spalvos režimo nustatymas";
                if (mode == ColorMode.BlackWhite && !(bool)ListAllows(item.Properties, IPA_DATATYPE, 0))
                {
                    ScanLog.Write("Nespalvotas režimas nepalaikomas, naudojamas pilkas");
                    result.Warnings.Add("Skeneris nepalaiko nespalvoto režimo, todėl nuskenuota pilkai.");
                    mode = ColorMode.Gray;
                }
                (int intent, int dataType, int depth) = mode switch
                {
                    ColorMode.Color => (1, 3, 24),
                    ColorMode.Gray => (2, 2, 8),
                    _ => (4, 0, 1)
                };
                SetProp(item.Properties, IPS_CUR_INTENT, intent, "elementas");
                SetProp(item.Properties, IPA_DATATYPE, dataType, "elementas");
                SetProp(item.Properties, IPA_DEPTH, depth, "elementas");

                // Raiška (būtina nustatyti PRIEŠ plotą). Toliau naudojama tik ta reikšmė,
                // kurią tvarkyklė iš tikrųjų paliko, nes nepalaikomą DPI ji tyliai atmeta.
                stage = "DPI nustatymas";
                SetProp(item.Properties, IPS_XRES, dpi, "elementas");
                SetProp(item.Properties, IPS_YRES, dpi, "elementas");
                object? xr = GetProp(item.Properties, IPS_XRES);
                object? yr = GetProp(item.Properties, IPS_YRES);
                int actualDpi = ToIntOr(xr, dpi);
                int actualDpiY = ToIntOr(yr, actualDpi);
                if (actualDpi <= 0) actualDpi = dpi;
                if (actualDpi != dpi)
                {
                    ScanLog.Write($"DĖMESIO: tvarkyklė paliko {actualDpi} DPI vietoj prašyto {dpi}. Plotas ir PDF skaičiuojami pagal {actualDpi} DPI");
                    result.Warnings.Add($"Skeneris nepalaiko {dpi} DPI, todėl nuskenuota {actualDpi} DPI.");
                }
                if (actualDpiY != actualDpi)
                    ScanLog.Write($"DĖMESIO: vertikalus DPI ({actualDpiY}) skiriasi nuo horizontalaus ({actualDpi})");
                result.ActualDpi = actualDpi;

                // Skenavimo plotas pagal TIKRĄJĮ DPI
                stage = "ploto skaičiavimas";
                double bedW = ToDouble(GetProp(device.Properties, useFeeder ? DPS_HORIZONTAL_SHEET_FEED_SIZE : DPS_HORIZONTAL_BED_SIZE)) / 1000.0;
                double bedH = ToDouble(GetProp(device.Properties, useFeeder ? DPS_VERTICAL_SHEET_FEED_SIZE : DPS_VERTICAL_BED_SIZE)) / 1000.0;
                double wIn = bedW > 0 ? bedW : 8.27, hIn = bedH > 0 ? bedH : 11.69;
                if (pageInches.HasValue)
                {
                    wIn = bedW > 0 ? Math.Min(pageInches.Value.Width, bedW) : pageInches.Value.Width;
                    hIn = bedH > 0 ? Math.Min(pageInches.Value.Height, bedH) : pageInches.Value.Height;
                }
                int expectedW = (int)(wIn * actualDpi), expectedH = (int)(hIn * actualDpi);
                ScanLog.Write($"Plotas ({(useFeeder ? "tiektuvas" : "stiklas")}): skeneris praneša {bedW:0.###} x {bedH:0.###} in; " +
                              $"naudojama {wIn:0.###} x {hIn:0.###} in → {expectedW} x {expectedH} px prie {actualDpi} DPI");

                stage = "ploto nustatymas";
                SetProp(item.Properties, IPS_XPOS, 0, "elementas");
                SetProp(item.Properties, IPS_YPOS, 0, "elementas");
                SetPropClamped(item.Properties, IPS_XEXTENT, expectedW, "elementas");
                SetPropClamped(item.Properties, IPS_YEXTENT, expectedH, "elementas");

                // Perdavimo formatas: suspaustas PNG, jei skeneris jį aiškiai palaiko (daug greičiau per tinklą)
                stage = "perdavimo formato nustatymas";
                bool usePng = false;
                if ((bool)ListContains(item.Properties, IPA_COMPRESSION, WIA_COMPRESSION_PNG))
                {
                    usePng = SetProp(item.Properties, IPA_COMPRESSION, WIA_COMPRESSION_PNG, "elementas");
                    if (usePng) SetProp(item.Properties, IPA_FORMAT, FormatPng, "elementas");
                }
                ScanLog.Write($"Perdavimo formatas: {(usePng ? "PNG (suspaustas)" : "BMP (nesuspaustas)")}");

                LogKeyProps("Skeneris PO nustatymų", device.Properties);
                LogKeyProps("Elementas PO nustatymų", item.Properties);

                dynamic dialog = Create("WIA.CommonDialog");
                const int MaxPages = 500; // apsauga nuo begalinio ciklo
                int n = 0;
                while (n < MaxPages)
                {
                    n++;
                    stage = $"perdavimas #{n}";
                    if (useFeeder) ScanLog.Write($"Prieš lapą #{n}: " + (string)FeederStatus(device.Properties));
                    ScanLog.Write($"Perdavimas #{n} pradėtas");
                    sw.Restart();
                    string format = usePng ? FormatPng : FormatBmp;
                    dynamic? img;
                    try
                    {
                        img = Retry<object?>($"ShowTransfer #{n}", () => dialog.ShowTransfer(item, format, false));
                    }
                    catch (Exception ex) when (HResult(ex) == WIA_ERROR_PAPER_EMPTY)
                    {
                        ScanLog.Write($"Perdavimas #{n}: tiektuvas tuščias (0x80210003) po {sw.ElapsedMilliseconds} ms. Nuskenuota lapų: {pages.Count}");
                        if (pages.Count == 0) throw new InvalidOperationException("Tiektuve (ADF) nėra lapų.");
                        break; // visi lapai iš tiektuvo nuskenuoti
                    }
                    catch (Exception ex) when (usePng && pages.Count == 0 && sw.ElapsedMilliseconds < 3000)
                    {
                        // Tvarkyklė PNG deklaruoja, bet greitai atmeta: grįžtame prie BMP ir bandome dar kartą.
                        ScanLog.Write($"Perdavimas #{n}: PNG atmestas per {sw.ElapsedMilliseconds} ms (0x{HResult(ex):X8} {ex.Message}), bandoma BMP");
                        usePng = false;
                        SetProp(item.Properties, IPA_COMPRESSION, 0, "elementas");
                        SetProp(item.Properties, IPA_FORMAT, FormatBmp, "elementas");
                        n--;
                        continue;
                    }
                    catch (Exception ex) when (useFeeder && pages.Count > 0)
                    {
                        // Kai kurios tvarkyklės (pvz., Microsoft WSD) po paskutinio lapo arba nutrūkus darbui
                        // grąžina bendrą klaidą (0x80004005) vietoj „tiektuvas tuščias“. Atskirti neįmanoma,
                        // todėl laikome tai darbo pabaiga ir išsaugome tai, kas jau nuskenuota.
                        ScanLog.Write($"Perdavimas #{n}: klaida 0x{HResult(ex):X8} ({ex.Message}) po {sw.ElapsedMilliseconds} ms. " +
                                      $"Laikoma tiektuvo darbo pabaiga, išsaugoma lapų: {pages.Count}");
                        result.Warnings.Add($"Nuskenuota lapų: {pages.Count}. Skeneris darbą užbaigė su klaida " +
                                            "(tai būdinga kai kurioms tinklo tvarkyklėms). Patikrinkite, ar nuskenuoti visi lapai.");
                        break;
                    }
                    if (img == null)
                    {
                        ScanLog.Write($"Perdavimas #{n}: atšaukta (grąžinta null) po {sw.ElapsedMilliseconds} ms");
                        break;
                    }
                    LogImage(n, img, sw.ElapsedMilliseconds, actualDpi, expectedW, expectedH);
                    byte[] data = (byte[])img.FileData.BinaryData;
                    ScanLog.Write($"  Gauta duomenų: {data.Length / 1024.0:N0} KB ({(usePng ? "PNG" : "BMP")})");
                    pages.Add(ToPage(data, actualDpi));
                    if (!useFeeder) break;
                }

                LogKeyProps("Elementas PO skenavimo", item.Properties);
                ScanLog.Write($"Skenavimas baigtas, lapų: {pages.Count}");
                return result;
            }
            catch (Exception ex)
            {
                ScanLog.Write($"KLAIDA etape „{stage}“: 0x{HResult(ex):X8} {ex.Message} (jau nuskenuota lapų: {pages.Count})");
                if (pages.Count > 0)
                {
                    // Neprarandame jau nuskenuotų lapų
                    result.Warnings.Add($"Skenavimas nutrūko: {Describe(ex)} Išsaugoti jau nuskenuoti lapai: {pages.Count}.");
                    return result;
                }
                throw;
            }
            finally
            {
                // Atleidžiame WIA objektus, kad kitas skenavimas negautų „busy“
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        /// <summary>Atsarginis variantas: standartinis Windows / tvarkyklės skenavimo langas.</summary>
        public static ScannedPage? ScanWithDialog()
        {
            ScanLog.Section("SKENAVIMAS PER TVARKYKLĖS LANGĄ");
            try
            {
                dynamic dialog = Create("WIA.CommonDialog");
                var sw = Stopwatch.StartNew();
                // DeviceType=skeneris, Intent=0, Bias=kokybė, formatas, AlwaysSelectDevice, UseCommonUI, CancelError
                dynamic? img = Retry<object?>("ShowAcquireImage",
                    () => dialog.ShowAcquireImage(ScannerDeviceType, 0, 131072, FormatBmp, false, true, false));
                if (img == null)
                {
                    ScanLog.Write("Atšaukta (grąžinta null)");
                    return null;
                }
                int reported = Convert.ToInt32(ToDouble(img.HorizontalResolution));
                int width = SafeInt(() => img.Width);
                int dpi = GuessDpi(width, reported);
                if (dpi != reported)
                    ScanLog.Write($"Vaizdo DPI ({reported}) netikėtinas {width} px pločiui, naudojamas {dpi} DPI (įvertinta pagal A4/Letter plotį)");
                LogImage(1, img, sw.ElapsedMilliseconds, dpi, 0, 0);
                return ToPage((byte[])img.FileData.BinaryData, dpi);
            }
            catch (Exception ex)
            {
                ScanLog.Write($"KLAIDA: 0x{HResult(ex):X8} {ex.Message}");
                throw;
            }
            finally
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        /// <param name="dpi">Tvarkyklės patvirtintas DPI. BMP antraštei nepasitikime:
        /// pvz., Microsoft WSD tvarkyklė ten visada įrašo 96 DPI.</param>
        private static ScannedPage ToPage(byte[] data, int dpi)
        {
            using var ms = new MemoryStream(data);
            using var tmp = new Bitmap(ms);
            float useDpi = dpi >= 50 ? dpi : (tmp.HorizontalResolution >= 50 ? tmp.HorizontalResolution : 200);
            return ScannedPage.FromImage(tmp, useDpi);
        }

        /// <summary>Jei vaizdo DPI akivaizdžiai neteisingas (lapas būtų platesnis nei ~37 cm), įvertina DPI pagal lapo plotį.</summary>
        private static int GuessDpi(int widthPx, int reported)
        {
            if (widthPx <= 0) return reported >= 50 ? reported : 200;
            if (reported >= 50 && widthPx / (double)reported <= 14.5) return reported;
            double estimate = widthPx / 8.5;
            int[] standard = { 75, 100, 150, 200, 240, 300, 400, 600, 1200 };
            return standard.OrderBy(v => Math.Abs(v - estimate)).First();
        }

        // ---------- žurnalo pagalbinės ----------

        private static dynamic? FindDeviceInfo(string deviceId)
        {
            dynamic manager = Create("WIA.DeviceManager");
            dynamic infos = manager.DeviceInfos;
            int count = Convert.ToInt32(infos.Count);
            for (int i = 1; i <= count; i++)
            {
                dynamic di = infos[i];
                if (Convert.ToString(di.DeviceID) == deviceId) return di;
            }
            return null;
        }

        private static void DumpItems(dynamic parent, string path, int depth)
        {
            int count = SafeInt(() => parent.Items.Count);
            if (count <= 0) return;
            for (int i = 1; i <= count; i++)
            {
                dynamic it = parent.Items[i];
                string p = $"{path}[{i}]";
                DumpProps($"Elementas {p}: {(string)ItemLabel(it)}", it.Properties);
                if (depth < 4) DumpItems(it, p, depth + 1);
            }
        }

        private static void DumpProps(string title, dynamic props)
        {
            var sb = new StringBuilder($"--- {title} ---");
            int count = SafeInt(() => props.Count);
            for (int i = 1; i <= count; i++)
            {
                try { sb.Append("\n    ").Append((string)FormatProp(props[i])); }
                catch (Exception ex) { sb.Append($"\n    (savybė #{i} neperskaitoma: 0x{HResult(ex):X8})"); }
            }
            ScanLog.Write(sb.ToString());
        }

        private static void LogKeyProps(string title, dynamic props)
        {
            if (!ScanLog.Enabled) return;
            var sb = new StringBuilder(title + ":");
            foreach (int id in KeyProps)
            {
                dynamic? p = FindProp(props, id);
                if (p != null) sb.Append("\n    ").Append((string)FormatProp(p));
            }
            ScanLog.Write(sb.ToString());
        }

        private static string ItemLabel(dynamic item)
        {
            string name = FormatValue(GetProp(item.Properties, IPA_ITEM_NAME));
            string id = SafeStr(() => item.ItemID);
            return $"pavadinimas={name}, ItemID={id}";
        }

        private static string FeederStatus(dynamic props)
        {
            object? st = GetProp(props, DPS_DOCUMENT_HANDLING_STATUS);
            object? sel = GetProp(props, DPS_DOCUMENT_HANDLING_SELECT);
            string s1 = st == null ? "būsena: savybės nėra" : $"būsena={FormatValue(st)}{DecodeFlags(DPS_DOCUMENT_HANDLING_STATUS, ToIntOr(st, -1))}";
            string s2 = sel == null ? "šaltinis: savybės nėra" : $"šaltinis={FormatValue(sel)}{DecodeFlags(DPS_DOCUMENT_HANDLING_SELECT, ToIntOr(sel, -1))}";
            return s1 + "; " + s2;
        }

        private static void LogImage(int n, dynamic img, long ms, int dpi, int expectedW, int expectedH)
        {
            int w = SafeInt(() => img.Width), h = SafeInt(() => img.Height);
            double hr = SafeDouble(() => img.HorizontalResolution), vr = SafeDouble(() => img.VerticalResolution);
            ScanLog.Write($"Perdavimas #{n} baigtas per {ms} ms: {w} x {h} px, {hr:0.#} x {vr:0.#} DPI");
            if (hr > 0 && Math.Abs(hr - dpi) > 1)
                ScanLog.Write($"  Pastaba: vaizdo antraštėje {hr:0.#} DPI, PDF'ui naudojamas {dpi} DPI");
            if (expectedW > 0 && w > 0 && w < expectedW * 0.95)
                ScanLog.Write($"  DĖMESIO: vaizdas siauresnis nei tikėtasi ({w} < {expectedW} px), galimas apkarpymas");
            if (expectedH > 0 && h > 0 && h < expectedH * 0.95)
                ScanLog.Write($"  DĖMESIO: vaizdas žemesnis nei tikėtasi ({h} < {expectedH} px), galimas apkarpymas");
        }

        private static string FormatProp(dynamic p)
        {
            int id = SafeInt(() => p.PropertyID);
            string name = SafeStr(() => p.Name);
            bool ro = SafeBool(() => p.IsReadOnly);

            object? raw = null;
            string val;
            try { raw = p.Value; val = FormatValue(raw); }
            catch (Exception ex) { val = $"(neperskaitoma, 0x{HResult(ex):X8})"; }

            var sb = new StringBuilder($"[{id}] {name} = {val}");
            sb.Append(DecodeFlags(id, ToIntOr(raw, -1)));
            if (ro) sb.Append("  (tik skaityti)");

            int sub = SafeInt(() => p.SubType); // 1 = rėžis, 2 = sąrašas, 3 = vėliavos
            if (sub == 1)
            {
                sb.Append($"  rėžis {SafeStr(() => p.SubTypeMin)}..{SafeStr(() => p.SubTypeMax)}, žingsnis {SafeStr(() => p.SubTypeStep)}");
            }
            else if (sub == 2 || sub == 3)
            {
                var vals = new List<string>();
                try
                {
                    dynamic v = p.SubTypeValues;
                    int c = Convert.ToInt32(v.Count);
                    for (int i = 1; i <= Math.Min(c, 40); i++) vals.Add(FormatValue((object?)v[i]));
                    if (c > 40) vals.Add("…");
                }
                catch { }
                sb.Append(sub == 2 ? "  galimos: " : "  vėliavos: ").Append(string.Join(", ", vals));
            }
            return sb.ToString();
        }

        private static string DecodeFlags(int id, int value)
        {
            string[]? names = id switch
            {
                DPS_DOCUMENT_HANDLING_CAPABILITIES => new[] { "FEED", "FLAT", "DUP", "DETECT_FLAT", "DETECT_SCAN", "DETECT_FEED", "DETECT_DUP", "DETECT_FEED_AVAIL", "DETECT_DUP_AVAIL" },
                DPS_DOCUMENT_HANDLING_STATUS => new[] { "FEED_READY", "FLAT_READY", "DUP_READY", "FLAT_COVER_UP", "PATH_COVER_UP", "PAPER_JAM" },
                DPS_DOCUMENT_HANDLING_SELECT => new[] { "FEEDER", "FLATBED", "DUPLEX", "FRONT_FIRST", "BACK_FIRST", "FRONT_ONLY", "BACK_ONLY", "NEXT_PAGE", "PREFEED", "AUTO_ADVANCE" },
                _ => null
            };
            if (names == null || value < 0) return "";
            var set = new List<string>();
            for (int b = 0; b < names.Length; b++)
                if ((value & (1 << b)) != 0) set.Add(names[b]);
            return "  → " + (set.Count > 0 ? string.Join(" | ", set) : "nieko");
        }

        private static string FormatValue(object? v) => v switch
        {
            null => "null",
            string s => $"\"{s}\"",
            IConvertible c => c.ToString(CultureInfo.InvariantCulture),
            _ => $"({v.GetType().Name})"
        };

        private static int ToIntOr(object? v, int fallback)
        {
            try { return v is IConvertible ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : fallback; }
            catch { return fallback; }
        }

        private static int SafeInt(Func<object?> f) { try { return Convert.ToInt32(f(), CultureInfo.InvariantCulture); } catch { return -1; } }
        private static double SafeDouble(Func<object?> f) { try { return Convert.ToDouble(f(), CultureInfo.InvariantCulture); } catch { return 0; } }
        private static bool SafeBool(Func<object?> f) { try { return Convert.ToBoolean(f(), CultureInfo.InvariantCulture); } catch { return false; } }
        private static string SafeStr(Func<object?> f) { try { return Convert.ToString(f(), CultureInfo.InvariantCulture) ?? ""; } catch { return "?"; } }

        // ---------- WIA savybių pagalbinės ----------

        private static dynamic? FindProp(dynamic props, int id)
        {
            int count = Convert.ToInt32(props.Count);
            for (int i = 1; i <= count; i++)
            {
                dynamic p = props[i];
                if (Convert.ToInt32(p.PropertyID) == id) return p;
            }
            return null;
        }

        /// <summary>Leistinos sveikųjų skaičių reikšmės (iš sąrašo arba iš rėžio pagal kandidatus).</summary>
        private static List<int> ReadAllowedInts(dynamic props, int id, int[] candidates)
        {
            var result = new List<int>();
            dynamic? p = FindProp(props, id);
            if (p == null) return result;
            int sub = SafeInt(() => p.SubType);
            try
            {
                if (sub == 2)
                {
                    dynamic v = p.SubTypeValues;
                    int c = Convert.ToInt32(v.Count);
                    for (int i = 1; i <= c; i++)
                    {
                        int x = ToIntOr((object?)v[i], -1);
                        if (x >= 0) result.Add(x);
                    }
                }
                else if (sub == 1)
                {
                    int min = SafeInt(() => p.SubTypeMin), max = SafeInt(() => p.SubTypeMax), step = SafeInt(() => p.SubTypeStep);
                    foreach (int c in candidates)
                        if (c >= min && c <= max && (step <= 1 || (c - min) % step == 0)) result.Add(c);
                }
                else
                {
                    int cur = ToIntOr((object?)p.Value, -1);
                    if (cur >= 0) result.Add(cur);
                }
            }
            catch { }
            return result.Distinct().OrderBy(x => x).ToList();
        }

        /// <summary>Ar reikšmė AIŠKIAI yra leistinų sąraše (nežinoma = ne).</summary>
        private static bool ListContains(dynamic props, int id, int value)
        {
            dynamic? p = FindProp(props, id);
            if (p == null) return false;
            if (SafeInt(() => p.SubType) != 2) return false;
            List<int> allowed = ReadAllowedInts(props, id, new[] { value });
            return allowed.Contains(value);
        }

        /// <summary>Ar savybė leidžia reikšmę. Jei nežinoma, laikoma, kad leidžia.</summary>
        private static bool ListAllows(dynamic props, int id, int value)
        {
            dynamic? p = FindProp(props, id);
            if (p == null) return true;
            int sub = SafeInt(() => p.SubType);
            if (sub != 1 && sub != 2) return true;
            List<int> allowed = ReadAllowedInts(props, id, new[] { value });
            return allowed.Count == 0 || allowed.Contains(value);
        }

        private static object? GetProp(dynamic props, int id)
        {
            dynamic? p = FindProp(props, id);
            return p?.Value;
        }

        /// <summary>Iš naujo perskaito savybę iš kolekcijos (ne iš to paties objekto), kad matytume tikrą reikšmę.</summary>
        private static string ReadBack(dynamic props, int id)
        {
            try
            {
                dynamic? q = FindProp(props, id);
                return q == null ? "?" : FormatValue((object?)q.Value);
            }
            catch (Exception ex) { return $"(neperskaitoma, 0x{HResult(ex):X8})"; }
        }

        private static bool SetProp(dynamic props, int id, object value, string where)
        {
            dynamic? p = FindProp(props, id);
            if (p == null)
            {
                ScanLog.Write($"SET {where} [{id}] = {value}: tokios savybės nėra");
                return false;
            }
            string name = SafeStr(() => p.Name);
            try
            {
                p.Value = value;
                ScanLog.Write($"SET {where} [{id}] {name} = {value}: priimta, dabar {(string)ReadBack(props, id)}");
                return true;
            }
            catch (Exception ex)
            {
                // tvarkyklė nepalaiko reikšmės, paliekam numatytąją
                ScanLog.Write($"SET {where} [{id}] {name} = {value}: ATMESTA 0x{HResult(ex):X8}, liko {(string)ReadBack(props, id)}");
                return false;
            }
        }

        private static void SetPropClamped(dynamic props, int id, int value, string where)
        {
            dynamic? p = FindProp(props, id);
            if (p == null)
            {
                ScanLog.Write($"SET {where} [{id}] = {value}: tokios savybės nėra");
                return;
            }
            string name = SafeStr(() => p.Name);
            int requested = value, max = -1;
            try
            {
                max = Convert.ToInt32(p.SubTypeMax);
                if (max > 0 && value > max) value = max;
            }
            catch { }
            string note = value != requested ? $" (prašyta {requested}, apribota iki max {max})" : $" (max {max})";
            try
            {
                p.Value = value;
                ScanLog.Write($"SET {where} [{id}] {name} = {value}{note}: priimta, dabar {(string)ReadBack(props, id)}");
            }
            catch (Exception ex)
            {
                ScanLog.Write($"SET {where} [{id}] {name} = {value}{note}: ATMESTA 0x{HResult(ex):X8}, liko {(string)ReadBack(props, id)}");
            }
        }

        private static double ToDouble(object? o)
        {
            try { return o == null ? 0 : Convert.ToDouble(o); } catch { return 0; }
        }

        private static int HResult(Exception ex) => ScanLog.HR(ex);

        /// <summary>Suprantamas WIA klaidų aprašymas.</summary>
        public static string Describe(Exception ex)
        {
            string? known = (uint)HResult(ex) switch
            {
                0x80210002 => "Užstrigo popierius.",
                0x80210003 => "Tiektuve nėra popieriaus.",
                0x80210004 => "Problema su popieriumi.",
                0x80210005 => "Skeneris išjungtas arba neprijungtas.",
                0x80210006 => "Skeneris užimtas (galbūt jį naudoja kita programa).",
                0x80210007 => "Skeneris šyla, pabandykite po kelių sekundžių.",
                0x8021000A => "Nepavyko susisiekti su skeneriu.",
                0x8021000C => "Netinkami skenerio nustatymai (pabandykite kitą DPI ar režimą).",
                0x8021000D => "Skeneris užrakintas.",
                0x80210015 => "Skenerio nėra arba jam neįdiegta WIA tvarkyklė.",
                _ => null
            };
            return known ?? $"{ex.Message} (0x{HResult(ex):X8})";
        }
    }
}
