# OfflineScan: skenavimas į PDF be interneto ir paskyrų

## Kaip veikia
- **Skenavimas:** per WIA (Windows Image Acquisition), kuri yra standartinė Windows dalis. Programa nesiunčia jokių duomenų į internetą.
- **Buferis:** nuskenuoti lapai laikomi atmintyje. Galima skenuoti kelis kartus, keisti lapų tvarką, pasukti, ištrinti ir pridėti paveikslėlių iš failų (JPG/PNG/TIFF).
- **PDF:** failą rašo `PdfWriter.cs` (~100 eilučių, be jokių bibliotekų). Puslapio dydis atitinka tikrą nuskenuoto lapo dydį.
- **Spausdinimas:** tinka bet kuris spausdintuvas, įskaitant „Microsoft Print to PDF".

## Kompiliavimas
1. Visual Studio 2022 → *Open → Project/Solution* → `OfflineScan.csproj`.
2. *Build → Build Solution* ir paleiskite (F5).
3. Norint platinti kitiems kompiuteriams be .NET diegimo, paleiskite:
   `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`
   Gausite vieną `OfflineScan.exe` failą.

## Skenerio paruošimas (vieną kartą)
- **USB:** Windows 10/11 dažniausiai pats įdiegia WIA tvarkyklę. HP atveju galima įdiegti „HP Basic/Full Feature driver" be HP Smart programos.
- **Tinklo MFP:** *Parametrai → Bluetooth ir įrenginiai → Spausdintuvai ir skeneriai → Pridėti įrenginį*. Windows naudoja savo WSD/eSCL tvarkyklę.
- Patikrinimas: jei skeneris veikia standartinėje „Windows Fax and Scan" programoje, veiks ir čia.

## Jei kas nors neveikia
- **Nuskenuojama tik dalis lapo:** pakeiskite „Formatas" į „Visas plotas" arba naudokite mygtuką „Skenuoti per tvarkyklės langą".
- **Skeneris neatpažįsta režimo arba DPI:** pabandykite 200 arba 300 DPI.
- **ADF nuskenuoja tik 1 lapą:** kai kurios tvarkyklės nepalaiko kelių lapų per WIA. Tada naudokite tvarkyklės langą.
