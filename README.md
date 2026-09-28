<a id="en"></a>

# OfflineScan

**[English](#en) · [Lietuviškai](#lt)**

**Scan paper documents to PDF. No accounts, no subscriptions, no cloud, no internet.**

[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-blue)](#requirements)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](#building-from-source)
[![Dependencies](https://img.shields.io/badge/dependencies-none-brightgreen)](#how-it-works)
[![License](https://img.shields.io/badge/license-MIT-green)](#license)

OfflineScan is a small, no-nonsense Windows program for one everyday job: put invoices, receipts, contracts and other paperwork through the scanner, fix page order and rotation, and save everything as a single clean PDF that is easy to archive, file and send to the tax authority.

It is a workhorse, not a showcase. There are no animations, no themes, no wizards and no "smart" features. There is a scanner, a list of pages and a *Save PDF* button.

> **Note:** the user interface is in **Lithuanian**, as the program was built primarily for accountants in Lithuania. Button names below are given in English with the Lithuanian label in brackets.

---

## Why this exists

Modern scanner software has become surprisingly hard to use for a task that used to be trivial:

- **Mandatory accounts.** Some vendor apps require every user to register and sign in before they can scan a single page.
- **Subscriptions.** Basic features like multi-page PDF or scan-to-file are increasingly moved behind monthly fees.
- **Online-only.** Many apps stop working when the computer is offline or the vendor's servers are unavailable.
- **No transparency.** It is often unclear what the software sends, where, and why.

For an accountant who just needs to scan a stack of receipts before the end of the month, none of this is acceptable. OfflineScan takes the opposite approach:

| | OfflineScan |
|---|---|
| Account or login | **None** |
| License fee or subscription | **None** |
| Internet connection | **Never used** |
| Telemetry, analytics, updates | **None** |
| Installation | **Not required.** Runs from a folder or a USB stick |
| Source code | **Fully open.** Every line is in this repository |

---

## Who it is for

- **Accountants and bookkeepers** digitising invoices, receipts, bank statements and contracts.
- **Small offices** that want one simple tool on every PC instead of a different vendor app per scanner.
- **Anyone** who wants to scan documents without handing personal or company data to a third-party service.

No technical knowledge is needed. If you can lift a scanner lid, you can use OfflineScan.

---

## Features

- **Any WIA-compatible scanner**: USB or network, flatbed or automatic document feeder (ADF). This covers most scanners and multifunction printers from HP, Canon, Epson, Brother, Lexmark, Xerox and others.
- **Multi-page ADF scanning**: the whole stack in the feeder is scanned in one job, page by page, until the feeder is empty.
- **Settings adapted to each scanner**: when you select a scanner, the program reads which resolutions and colour modes it actually supports and offers only those. No more silently cropped pages because of an unsupported DPI.
- **Page buffer**: scan as many times as you need, from the flatbed and the feeder. Pages are collected in a list until you save.
- **Basic page editing**: move pages up and down, rotate 90°, delete, clear all.
- **Import images**: add JPG, PNG, BMP or TIFF files (including multi-page TIFF) to the same document.
- **Direct PDF export**: one click, one multi-page PDF. The page size in the PDF matches the real physical size of the paper (e.g. A4 = 210 × 297 mm).
- **Print**: to any printer, including *Microsoft Print to PDF*.
- **Faster network scanning**: if the scanner supports compressed transfer (PNG), the program uses it automatically, reducing the data sent over the network several times.
- **Robust error handling**: waits and retries when the scanner is busy or warming up, keeps already scanned pages if a problem occurs mid-stack, and shows clear messages ("feeder is empty", "paper jam").
- **Refresh button** (↻): releases all scanner connections and re-reads the scanner list, so most problems can be fixed without restarting the program.
- **Diagnostic log**: optional plain-text log next to the program, useful when a particular scanner driver misbehaves.

---

## Requirements

| | Minimum |
|---|---|
| Operating system | Windows 10 or Windows 11 (64-bit) |
| Hardware | Any PC that runs Windows 10. No GPU, no special CPU, very little RAM |
| Scanner | Any scanner with a WIA driver. If it works in *Windows Fax and Scan*, it works here |
| Runtime | None for the single-file build. The small build needs the free [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) |
| Disk space | About 60–150 MB for the single-file build, under 1 MB for the small build |

No vendor software (HP Smart, HP Scan, etc.) is required. In most cases Windows installs a suitable driver automatically. Network scanners can be added through *Settings → Bluetooth & devices → Printers & scanners → Add device*.

### Tested scanners

| Scanner | Connection | Driver | Flatbed | ADF (multi-page) |
|---|---|---|---|---|
| HP LaserJet Pro MFP M225dw | Network | Microsoft WSD (built into Windows) | ✅ | ✅ |
| HP LaserJet MFP M426fdn | Network | Microsoft WSD (built into Windows) | ✅ | ✅ |
| Lexmark MX410de | Network | Lexmark WIA driver | ✅ | ✅ |

Reports from other models are welcome. See [Diagnostic log](#diagnostic-log).

---

## Quick start

1. Download `OfflineScan.exe` from the [Releases](../../releases) page, or [build it yourself](#building-from-source).
2. Copy it anywhere: a folder on the PC, a network share, or a USB stick.
3. Run it. Windows SmartScreen may warn about an unsigned program the first time. Click *More info → Run anyway*.
4. Select your scanner and settings. To use the document feeder, tick **Feeder (ADF)** (*Tiektuvas (ADF)*).
5. Click **Scan** (*Skenuoti*). Repeat for more pages, then rotate or reorder them as needed.
6. Click **Save PDF…** (*Išsaugoti PDF…*).

Nothing is installed, nothing is registered, nothing is written to the Windows registry.

---

## Recommended settings for accounting documents

| Document | Resolution | Mode | Notes |
|---|---|---|---|
| Invoices, contracts, letters | 200–300 DPI | Greyscale | Good readability, small files |
| Receipts and till slips | 300 DPI | Greyscale | Thermal paper is faint. Greyscale keeps detail |
| Documents with coloured stamps or signatures | 300 DPI | Colour | Larger files, slower on some network scanners |
| Plain typed text only | 300 DPI | Black & white | Smallest files (if the scanner supports this mode) |

A JPEG quality of **75–85** gives a good balance between file size and readability. For documents submitted to tax authorities, 300 DPI greyscale is a safe default.

---

## How it works

OfflineScan is intentionally small and uses only what is already part of Windows.

- **Flatbed scanning** uses the classic **WIA (Windows Image Acquisition)** automation interface through late-bound COM, so no extra libraries or COM references are needed.
- **Feeder scanning** uses the native **WIA 2.0** interface (the same one used by *Windows Fax and Scan*). All pages are transferred in a single job, which is the only reliable way to scan a stack with many network scanners. It runs on a background thread so the window stays responsive. If WIA 2.0 cannot be used with a particular scanner, the program automatically falls back to the classic interface.
- **PDF output** is produced by a built-in writer of about a hundred lines of code (`PdfWriter.cs`). Each page is stored as a JPEG image inside a standard PDF 1.4 file. There are no third-party PDF libraries, so there is nothing hidden to audit.
- **All pages stay in memory** until you save. Nothing is uploaded, cached online or sent anywhere.

### Project structure

```
OfflineScan/
├── OfflineScan.csproj   Project file (.NET 8, Windows Forms)
├── Program.cs           Entry point
├── MainForm.cs          The single program window
├── WiaScanner.cs        Scanner list, capabilities, flatbed scanning (classic WIA)
├── WiaAdf2.cs           Multi-page feeder scanning (WIA 2.0)
├── ScannedPage.cs       One page in the buffer (image + DPI)
├── PdfWriter.cs         Minimal dependency-free PDF writer
└── ScanLog.cs           Optional diagnostic log
```

---

## Building from source

You need **Visual Studio 2022** (the free Community edition is fine) with the *.NET desktop development* workload, or just the **.NET 8 SDK**.

**In Visual Studio:** open `OfflineScan.csproj` and press **F5**.

**From the command line:**

```bash
dotnet build -c Release
```

### Publishing a single portable .exe

```bash
dotnet publish -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:DebugType=none
```

The result is one file in `bin\Release\net8.0-windows\win-x64\publish\OfflineScan.exe`. It runs on any 64-bit Windows 10/11 PC without installing .NET.

### Publishing a small build

```bash
dotnet publish -c Release --self-contained false
```

Copy `OfflineScan.exe`, `OfflineScan.dll`, `OfflineScan.runtimeconfig.json` and `OfflineScan.deps.json`. The target PC needs the .NET 8 Desktop Runtime.

---

## Diagnostic log

If the **Log** (*Žurnalas*) checkbox is enabled, the program writes a plain-text log to a `Logs` folder next to the executable. If that location is read-only (for example, a write-protected USB stick), it falls back to `Documents\OfflineScan\Logs`. Each program start creates a new log file.

The log records which scanner items and settings the driver reports, which settings it accepted or rejected, how long each page took, how much data was received, and the exact error codes. It contains no document images.

The **Diagnostics** (*Diagnostika*) button writes a full description of the selected scanner without scanning anything. If you report a problem with a specific scanner, please attach this log.

---

## Known limitations

- **Windows only.** WIA is a Windows technology.
- **Duplex (two-sided) scanning** is not yet available. The feeder scans the front side only.
- **eSCL-only devices are not listed.** Some newer network printers appear in Windows only as eSCL ("AirScan") devices without a WIA driver. These are currently not shown in the scanner list.
- **After the last sheet, the scanner tries to feed one more.** The program asks the scanner to scan until the feeder is empty, so the scanner only discovers the end of the stack by trying to pick up another sheet. This is normal and *Windows Fax and Scan* behaves the same way.
- **Pages appear after the whole stack is scanned.** During feeder scanning, progress is shown in the status bar, and the page thumbnails appear when the job is finished.
- **No OCR.** PDFs contain images, not searchable text.
- **PDF pages are always JPEG images.** This keeps the writer simple and the files compact. It is not a PDF/A archival writer.

---

## Privacy

OfflineScan makes **no internet connections of any kind**. It does not check for updates, collect statistics, or contact any server. Network scanners are reached only through the Windows scanner service on your local network, exactly as with *Windows Fax and Scan*. Scanned documents exist only in memory and in the PDF files you choose to save. You can verify all of this by reading the source code.

---

## Contributing

Bug reports, diagnostic logs from different scanners, and pull requests are welcome.

When contributing, please keep the spirit of the project:

- **No new runtime dependencies** unless there is a very strong reason.
- **No internet features.**
- **No accounts, licensing checks or telemetry.**
- **Keep the interface plain.** The target user is an accountant with a stack of receipts, not a power user.

---

## License

Released under the [MIT License](LICENSE). Free to use, copy, modify and distribute, including in commercial settings, with no fees and no registration.

---
---

<a id="lt"></a>

# OfflineScan (lietuviškai)

**[English](#en) · [Lietuviškai](#lt)**

**Popierinių dokumentų skenavimas į PDF. Be paskyrų, be prenumeratų, be debesies, be interneto.**

OfflineScan yra nedidelė, paprasta Windows programa vienam kasdieniam darbui: nuskenuoti sąskaitas faktūras, čekius, sutartis ir kitus dokumentus, sutvarkyti puslapių eilę ir pasukimą, ir išsaugoti viską kaip vieną tvarkingą PDF failą. Tokį failą lengva suarchyvuoti, susisteminti ir pateikti Valstybinei mokesčių inspekcijai (VMI) ar auditoriui.

Tai darbinis arkliukas, o ne demonstracija. Čia nėra animacijų, temų, vedlių ar „išmaniųjų" funkcijų. Yra skeneris, puslapių sąrašas ir mygtukas *Išsaugoti PDF*.

---

## Kodėl ši programa atsirado

Šiuolaikinė skenerių programinė įranga stebėtinai apsunkino darbą, kuris anksčiau buvo visiškai paprastas:

- **Privalomos paskyros.** Kai kurios gamintojų programos reikalauja, kad kiekvienas vartotojas užsiregistruotų ir prisijungtų, prieš nuskenuodamas bent vieną lapą.
- **Prenumeratos.** Net pagrindinės funkcijos, pvz., kelių puslapių PDF ar skenavimas į failą, vis dažniau tampa mokamos kas mėnesį.
- **Veikia tik prisijungus prie interneto.** Daug programų nustoja veikti, kai kompiuteris neturi interneto arba gamintojo serveriai nepasiekiami.
- **Neaišku, ką jos daro.** Dažnai nežinia, kokius duomenis programa siunčia, kur ir kodėl.

Apskaitininkei, kuriai mėnesio pabaigoje tiesiog reikia nuskenuoti krūvelę čekių, visa tai nepriimtina. OfflineScan eina priešingu keliu:

| | OfflineScan |
|---|---|
| Paskyra ar prisijungimas | **Nereikia** |
| Licencijos mokestis ar prenumerata | **Nėra** |
| Interneto ryšys | **Niekada nenaudojamas** |
| Duomenų rinkimas, statistika, atnaujinimai | **Nėra** |
| Diegimas | **Nereikalingas.** Veikia iš aplanko ar USB laikmenos |
| Programos kodas | **Visiškai atviras.** Kiekviena eilutė yra šioje repozitorijoje |

---

## Kam ji skirta

- **Buhalterėms ir apskaitininkėms**, kurios skaitmenina sąskaitas faktūras, kasos čekius, banko išrašus ir sutartis.
- **Mažoms įmonėms ir biurams**, kurios nori vienos paprastos programos visuose kompiuteriuose, užuot kiekvienam skeneriui diegusios skirtingą gamintojo programą.
- **Visiems**, kurie nori skenuoti dokumentus neatiduodami asmeninių ar įmonės duomenų trečiųjų šalių paslaugoms.

Techninių žinių nereikia. Jei mokate atidaryti skenerio dangtį, mokėsite naudotis ir OfflineScan.

---

## Galimybės

- **Bet kuris WIA suderinamas skeneris:** USB ar tinklo, per stiklą ar automatinį lapų tiektuvą (ADF). Tai apima daugumą HP, Canon, Epson, Brother, Lexmark, Xerox ir kitų gamintojų skenerių bei daugiafunkcinių įrenginių.
- **Kelių lapų skenavimas iš tiektuvo:** visa tiektuve esanti krūvelė nuskenuojama vienu darbu, lapas po lapo, kol tiektuvas ištuštėja.
- **Nustatymai pagal konkretų skenerį:** pasirinkus skenerį, programa perskaito, kokius DPI ir spalvų režimus jis iš tikrųjų palaiko, ir siūlo tik juos. Nebėra tyliai apkarpytų lapų dėl nepalaikomo DPI.
- **Puslapių buferis:** skenuokite kiek reikia kartų, ir nuo stiklo, ir iš tiektuvo. Puslapiai kaupiami sąraše, kol juos išsaugosite.
- **Paprastas puslapių tvarkymas:** perkelti aukštyn ar žemyn, pasukti 90°, ištrinti, išvalyti viską.
- **Paveikslėlių pridėjimas:** į tą patį dokumentą galima įkelti JPG, PNG, BMP ar TIFF failus (taip pat daugiapuslapius TIFF).
- **Tiesioginis eksportas į PDF:** vienas paspaudimas, vienas kelių puslapių PDF failas. Puslapio dydis PDF'e atitinka tikrą lapo dydį (pvz., A4 = 210 × 297 mm).
- **Spausdinimas:** į bet kurį spausdintuvą, taip pat į *Microsoft Print to PDF*.
- **Greitesnis skenavimas per tinklą:** jei skeneris palaiko suspaustą perdavimą (PNG), programa jį naudoja automatiškai, ir per tinklą siunčiama kelis kartus mažiau duomenų.
- **Patikimas klaidų valdymas:** kai skeneris užimtas ar šyla, programa palaukia ir bando dar kartą. Jei problema atsiranda krūvelės viduryje, jau nuskenuoti lapai išsaugomi. Pranešimai aiškūs: „tiektuve nėra lapų", „užstrigo popierius".
- **Atnaujinimo mygtukas** (↻): atleidžia visus ryšius su skeneriais ir iš naujo surenka jų sąrašą, todėl dauguma problemų išsprendžiamos neperkraunant programos.
- **Diagnostinis žurnalas:** neprivalomas, paprasto teksto, saugomas šalia programos. Praverčia, kai konkretaus skenerio tvarkyklė elgiasi keistai.

---

## Reikalavimai

| | Minimalūs reikalavimai |
|---|---|
| Operacinė sistema | Windows 10 arba Windows 11 (64 bitų) |
| Kompiuteris | Bet koks kompiuteris, kuriame veikia Windows 10. Nereikia galingos vaizdo plokštės, ypatingo procesoriaus ar daug atminties |
| Skeneris | Bet koks skeneris su WIA tvarkykle. Jei jis veikia programoje *Windows faksas ir skenavimas*, veiks ir čia |
| Papildoma programinė įranga | Vieno failo versijai nereikia nieko. Mažajai versijai reikia nemokamo [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) |
| Vieta diske | Apie 60–150 MB vieno failo versijai, mažiau nei 1 MB mažajai versijai |

Gamintojų programų (HP Smart, HP Scan ir pan.) nereikia. Dažniausiai Windows tinkamą tvarkyklę įdiegia automatiškai. Tinklo skenerius galima pridėti per *Parametrai → Bluetooth ir įrenginiai → Spausdintuvai ir skeneriai → Pridėti įrenginį*.

### Išbandyti skeneriai

| Skeneris | Prijungimas | Tvarkyklė | Stiklas | ADF (keli lapai) |
|---|---|---|---|---|
| HP LaserJet Pro MFP M225dw | Tinklas | Microsoft WSD (Windows dalis) | ✅ | ✅ |
| HP LaserJet MFP M426fdn | Tinklas | Microsoft WSD (Windows dalis) | ✅ | ✅ |
| Lexmark MX410de | Tinklas | Lexmark WIA tvarkyklė | ✅ | ✅ |

Laukiami pranešimai ir apie kitus modelius (žr. skyrių „Diagnostinis žurnalas").

---

## Kaip pradėti

1. Atsisiųskite `OfflineScan.exe` iš [Releases](../../releases) puslapio arba sukompiliuokite patys (žr. skyrių „Kompiliavimas iš kodo").
2. Nukopijuokite failą bet kur: į aplanką kompiuteryje, bendrą tinklo aplanką ar USB laikmeną.
3. Paleiskite. Pirmą kartą Windows SmartScreen gali įspėti apie nepasirašytą programą. Spauskite *Daugiau informacijos → Vis tiek paleisti*.
4. Pasirinkite skenerį ir nustatymus. Jei norite skenuoti iš tiektuvo, pažymėkite **Tiektuvas (ADF)**.
5. Spauskite **Skenuoti**. Jei reikia, skenuokite daugiau lapų, pasukite ar sudėliokite juos reikiama tvarka.
6. Spauskite **Išsaugoti PDF…**.

Niekas nediegiama, niekur neregistruojama, į Windows registrą nieko nerašoma.

---

## Rekomenduojami nustatymai apskaitos dokumentams

| Dokumentas | Raiška | Režimas | Pastabos |
|---|---|---|---|
| Sąskaitos faktūros, sutartys, raštai | 200–300 DPI | Pilkai | Gerai įskaitoma, maži failai |
| Kasos čekiai | 300 DPI | Pilkai | Terminis popierius blankus, pilkas režimas išsaugo detales |
| Dokumentai su spalvotais antspaudais ar parašais | 300 DPI | Spalvotai | Didesni failai, kai kurie tinklo skeneriai skenuoja lėčiau |
| Tik spausdintas tekstas | 300 DPI | Nespalvotai | Mažiausi failai (jei skeneris palaiko šį režimą) |

JPEG kokybė **75–85** yra geras kompromisas tarp failo dydžio ir įskaitomumo. Dokumentams, teikiamiems VMI, saugus pasirinkimas yra 300 DPI, pilkai.

**Patarimas archyvui:** pavadinkite PDF failus vienodai, pvz., `2026-09_Tiekejas_SF-12345.pdf`. Programa pagal nutylėjimą siūlo pavadinimą su data ir laiku, kurį galite pakeisti išsaugodami.

---

## Kaip tai veikia

OfflineScan sąmoningai yra maža ir naudoja tik tai, kas jau yra Windows sistemoje.

- **Skenavimas nuo stiklo** vyksta per klasikinę **WIA (Windows Image Acquisition)** sąsają, naudojant vėlyvojo susiejimo COM. Papildomų bibliotekų nereikia.
- **Skenavimas iš tiektuvo** vyksta per **WIA 2.0** sąsają, tą pačią, kurią naudoja *Windows faksas ir skenavimas*. Visi lapai perduodami vienu darbu. Tai vienintelis patikimas būdas nuskenuoti krūvelę lapų su daugeliu tinklo skenerių. Skenavimas vyksta fone, todėl langas neužšąla. Jei su konkrečiu skeneriu WIA 2.0 panaudoti nepavyksta, programa automatiškai grįžta prie klasikinės sąsajos.
- **PDF failą** kuria integruotas, maždaug šimto eilučių rašytojas (`PdfWriter.cs`). Kiekvienas puslapis įrašomas kaip JPEG paveikslėlis standartiniame PDF 1.4 faile. Trečiųjų šalių PDF bibliotekų nėra, todėl nėra ir nieko paslėpto.
- **Visi puslapiai laikomi atmintyje**, kol juos išsaugosite. Niekas neįkeliama į internetą ir niekur nesiunčiama.

Projekto failų sandara aprašyta [angliškoje dalyje](#project-structure).

---

## Kompiliavimas iš kodo

Reikės **Visual Studio 2022** (tinka nemokama Community versija) su *.NET desktop development* komponentu arba tik **.NET 8 SDK**.

**Visual Studio:** atidarykite `OfflineScan.csproj` ir spauskite **F5**.

**Vieno failo .exe paruošimas:**

```bash
dotnet publish -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:DebugType=none
```

Rezultatas yra vienas failas `bin\Release\net8.0-windows\win-x64\publish\OfflineScan.exe`. Jis veikia bet kuriame 64 bitų Windows 10/11 kompiuteryje be .NET diegimo.

**Mažoji versija:**

```bash
dotnet publish -c Release --self-contained false
```

Nukopijuokite `OfflineScan.exe`, `OfflineScan.dll`, `OfflineScan.runtimeconfig.json` ir `OfflineScan.deps.json`. Kompiuteryje turi būti įdiegtas .NET 8 Desktop Runtime.

---

## Diagnostinis žurnalas

Jei pažymėta varnelė **Žurnalas**, programa rašo paprasto teksto žurnalą į aplanką `Logs` šalia programos failo. Jei ten rašyti negalima (pvz., USB laikmena apsaugota nuo rašymo), naudojamas aplankas `Dokumentai\OfflineScan\Logs`. Kiekvienas programos paleidimas kuria naują žurnalo failą.

Žurnale fiksuojama, kokius elementus ir nustatymus praneša skenerio tvarkyklė, kuriuos nustatymus ji priėmė ar atmetė, kiek laiko užtruko kiekvienas lapas, kiek duomenų gauta, ir tikslūs klaidų kodai. Nuskenuotų dokumentų vaizdų žurnale nėra.

Mygtukas **Diagnostika** surašo pilną pasirinkto skenerio aprašymą nieko neskenuodamas. Jei pranešate apie problemą su konkrečiu skeneriu, pridėkite šį žurnalą.

---

## Žinomi apribojimai

- **Veikia tik Windows.** WIA yra Windows technologija.
- **Dvipusis skenavimas** kol kas neprieinamas. Iš tiektuvo skenuojama tik priekinė lapo pusė.
- **Įrenginiai, pasiekiami tik per eSCL, sąraše nerodomi.** Kai kurie naujesni tinklo spausdintuvai Windows sistemoje matomi tik kaip eSCL („AirScan") įrenginiai be WIA tvarkyklės. Kol kas jų skenerių sąraše nėra.
- **Po paskutinio lapo skeneris bando įtraukti dar vieną.** Programa prašo skenuoti, kol tiektuvas ištuštės, todėl skeneris krūvelės pabaigą atpažįsta tik bandydamas paimti kitą lapą. Tai normalu, taip elgiasi ir *Windows faksas ir skenavimas*.
- **Lapai sąraše atsiranda nuskenavus visą krūvelę.** Skenuojant iš tiektuvo, eiga rodoma būsenos juostoje, o lapų miniatiūros atsiranda darbo pabaigoje.
- **Nėra teksto atpažinimo (OCR).** PDF failuose yra vaizdai, o ne tekstas.
- **PDF puslapiai visada yra JPEG vaizdai.** Tai leidžia rašytoją išlaikyti paprastą, o failus kompaktiškus. Tai nėra PDF/A archyvinio formato rašytojas.

---

## Privatumas

OfflineScan **nejungia jokių interneto ryšių**. Ji netikrina atnaujinimų, nerenka statistikos ir nesikreipia į jokį serverį. Tinklo skeneriai pasiekiami tik per Windows skenerių paslaugą vietiniame tinkle, lygiai taip pat kaip su *Windows faksas ir skenavimas*. Nuskenuoti dokumentai egzistuoja tik kompiuterio atmintyje ir tuose PDF failuose, kuriuos patys išsaugote. Visa tai galite patikrinti perskaitę programos kodą.

---

## Prisidėjimas

Laukiami pranešimai apie klaidas, diagnostiniai žurnalai iš įvairių skenerių ir kodo pakeitimai (pull requests).

Prisidėdami laikykitės projekto dvasios:

- **Jokių naujų priklausomybių**, nebent tam yra labai svari priežastis.
- **Jokių interneto funkcijų.**
- **Jokių paskyrų, licencijų tikrinimo ar duomenų rinkimo.**
- **Paprasta sąsaja.** Tikslinė vartotoja yra apskaitininkė su krūvele čekių, o ne kompiuterių specialistė.

---

## Licencija

Platinama pagal [MIT licenciją](LICENSE). Galima laisvai naudoti, kopijuoti, keisti ir platinti, taip pat komerciniais tikslais, be jokių mokesčių ir registracijos.
