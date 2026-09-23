using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.IO;
using System.Windows.Forms;

namespace OfflineScan
{
    public sealed class MainForm : Form
    {
        // Buferis: visi nuskenuoti puslapiai laikomi atmintyje, kol išsaugosite PDF
        private readonly List<ScannedPage> _pages = new();

        private readonly ComboBox _cbScanner = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 460 };
        private readonly ComboBox _cbDpi = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
        private readonly ComboBox _cbMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
        private readonly ComboBox _cbSize = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
        private readonly CheckBox _chkFeeder = new() { Text = "Tiektuvas (ADF)", AutoSize = true, Margin = new Padding(6, 7, 3, 3) };
        private readonly CheckBox _chkLog = new() { Text = "Žurnalas", Checked = true, AutoSize = true, Margin = new Padding(12, 7, 3, 3) };
        private readonly NumericUpDown _numQuality = new() { Minimum = 30, Maximum = 100, Value = 85, Width = 55 };
        private readonly ListView _list = new() { View = View.LargeIcon, MultiSelect = false, HideSelection = false, Dock = DockStyle.Fill };
        private readonly ImageList _thumbs = new() { ImageSize = new Size(96, 128), ColorDepth = ColorDepth.Depth24Bit };
        private readonly PictureBox _preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.DimGray };
        private readonly ToolStripStatusLabel _status = new() { Text = "Pasiruošęs" };

        public MainForm()
        {
            Text = "Skenavimas į PDF (be interneto)";
            Width = 1150;
            Height = 780;
            StartPosition = FormStartPosition.CenterScreen;

            _cbDpi.Items.AddRange(new object[] { 150, 200, 300, 600 });
            _cbDpi.SelectedItem = 300;
            _cbMode.Items.AddRange(new object[] { "Spalvotai", "Pilkai", "Nespalvotai (tekstas)" });
            _cbMode.SelectedIndex = 1;
            _cbSize.Items.AddRange(new object[] { "A4", "Letter", "Visas plotas" });
            _cbSize.SelectedIndex = 0;

            _chkLog.CheckedChanged += (_, _) =>
            {
                ScanLog.Enabled = _chkLog.Checked;
                ScanLog.Write("Žurnalas įjungtas");
            };

            _list.LargeImageList = _thumbs;
            _list.SelectedIndexChanged += (_, _) => ShowPreview();
            _list.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) DeleteSelected(); };

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
            top.Controls.AddRange(new Control[]
            {
                Lbl("Skeneris:"), _cbScanner, Btn("↻", (_, _) => LoadScanners()),
                Lbl("DPI:"), _cbDpi, Lbl("Režimas:"), _cbMode, Lbl("Formatas:"), _cbSize, _chkFeeder,
                Btn("Skenuoti", (_, _) => DoScan()),
                Btn("Skenuoti per tvarkyklės langą", (_, _) => DoScanDialog()),
                Btn("Pridėti iš failų…", (_, _) => ImportFiles())
            });

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6) };
            bottom.Controls.AddRange(new Control[]
            {
                Btn("▲ Aukštyn", (_, _) => MoveSelected(-1)),
                Btn("▼ Žemyn", (_, _) => MoveSelected(+1)),
                Btn("⟳ Pasukti 90°", (_, _) => RotateSelected()),
                Btn("Ištrinti", (_, _) => DeleteSelected()),
                Btn("Išvalyti viską", (_, _) => ClearAll()),
                Lbl("   JPEG kokybė:"), _numQuality,
                Btn("Išsaugoti PDF…", (_, _) => SavePdf()),
                Btn("Spausdinti…", (_, _) => PrintPages()),
                _chkLog,
                Btn("Diagnostika", (_, _) => DoDiagnose()),
                Btn("Atidaryti žurnalą", (_, _) => OpenLog())
            });

            var split = new SplitContainer { Dock = DockStyle.Fill };
            split.Panel1.Controls.Add(_list);
            split.Panel2.Controls.Add(_preview);

            var statusStrip = new StatusStrip();
            statusStrip.Items.Add(_status);

            // Dock tvarka: Fill pridedamas pirmas
            Controls.Add(split);
            Controls.Add(bottom);
            Controls.Add(top);
            Controls.Add(statusStrip);

            Load += (_, _) => { split.SplitterDistance = 340; ScanLog.StartSession(); LoadScanners(); };
            FormClosing += (_, e) =>
            {
                if (_pages.Count > 0 && MessageBox.Show(this, "Buferyje yra neišsaugotų puslapių. Išeiti?",
                        "Patvirtinimas", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    e.Cancel = true;
            };
        }

        // ---------- skenavimas ----------

        private void LoadScanners()
        {
            _cbScanner.Items.Clear();
            RunBusy(() =>
            {
                foreach (var s in WiaScanner.ListScanners()) _cbScanner.Items.Add(s);
                if (_cbScanner.Items.Count > 0) _cbScanner.SelectedIndex = 0;

                int maxWidth = _cbScanner.Width;
                foreach (var item in _cbScanner.Items)
                    maxWidth = Math.Max(maxWidth, TextRenderer.MeasureText(item.ToString(), _cbScanner.Font).Width + 25);
                _cbScanner.DropDownWidth = maxWidth;



                SetStatus(_cbScanner.Items.Count == 0
                    ? "Skenerių nerasta. Patikrinkite, ar skeneris įjungtas ir matomas Windows nustatymuose."
                    : $"Rasta skenerių: {_cbScanner.Items.Count}");
            });
        }

        private void DoScan()
        {
            if (_cbScanner.SelectedItem is not ScannerInfo scanner)
            {
                MessageBox.Show(this, "Pasirinkite skenerį.", "Skenavimas");
                return;
            }
            var mode = _cbMode.SelectedIndex switch { 0 => ColorMode.Color, 1 => ColorMode.Gray, _ => ColorMode.BlackWhite };
            SizeF? size = _cbSize.SelectedIndex switch { 0 => new SizeF(8.27f, 11.69f), 1 => new SizeF(8.5f, 11f), _ => null };
            int dpi = (int)_cbDpi.SelectedItem!;
            ScanLog.Write($"UI: skenuoti, skeneris=\"{scanner.Name}\", DPI={dpi}, režimas={_cbMode.SelectedItem}, " +
                          $"formatas={_cbSize.SelectedItem}, ADF={_chkFeeder.Checked}");
            RunBusy(() =>
            {
                SetStatus("Skenuojama…");
                AddPages(WiaScanner.Scan(scanner.Id, dpi, mode, _chkFeeder.Checked, size));
            });
        }

        private void DoDiagnose()
        {
            if (_cbScanner.SelectedItem is not ScannerInfo scanner)
            {
                MessageBox.Show(this, "Pasirinkite skenerį.", "Diagnostika");
                return;
            }
            if (!_chkLog.Checked) _chkLog.Checked = true;
            RunBusy(() =>
            {
                SetStatus("Renkama diagnostika…");
                ScanLog.Write($"UI: diagnostika, skeneris=\"{scanner.Name}\"");
                WiaScanner.Diagnose(scanner.Id);
                SetStatus($"Diagnostika įrašyta: {ScanLog.CurrentFile}");
            });
        }

        private void OpenLog()
        {
            try
            {
                string? file = ScanLog.CurrentFile;
                string target = file != null && File.Exists(file) ? file : ScanLog.Folder;
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Nepavyko atidaryti žurnalo: {ex.Message}\n{ScanLog.Folder}", "Žurnalas");
            }
        }

        private void DoScanDialog()
        {
            RunBusy(() =>
            {
                var page = WiaScanner.ScanWithDialog();
                if (page != null) AddPages(new List<ScannedPage> { page });
            });
        }

        private void ImportFiles()
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "Paveikslėliai|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff|Visi failai|*.*",
                Multiselect = true
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            RunBusy(() =>
            {
                var list = new List<ScannedPage>();
                foreach (var f in dlg.FileNames) list.AddRange(ScannedPage.FromFile(f));
                AddPages(list);
            });
        }

        private void AddPages(List<ScannedPage> pages)
        {
            if (pages.Count == 0) { SetStatus("Nieko nenuskenuota."); return; }
            _pages.AddRange(pages);
            RefreshList(_pages.Count - 1);
            SetStatus($"Pridėta puslapių: {pages.Count}. Iš viso buferyje: {_pages.Count}.");
        }

        // ---------- buferio tvarkymas ----------

        private int Sel => _list.SelectedIndices.Count > 0 ? _list.SelectedIndices[0] : -1;

        private void RefreshList(int select)
        {
            _preview.Image = null;
            _list.BeginUpdate();
            _list.Items.Clear();
            _thumbs.Images.Clear();
            for (int i = 0; i < _pages.Count; i++)
            {
                using var t = MakeThumb(_pages[i].Image);
                _thumbs.Images.Add(t);
                _list.Items.Add(new ListViewItem($"{i + 1} psl.", i));
            }
            _list.EndUpdate();
            if (select >= 0 && select < _pages.Count)
            {
                _list.Items[select].Selected = true;
                _list.Items[select].EnsureVisible();
            }
        }

        private Bitmap MakeThumb(Image src)
        {
            var size = _thumbs.ImageSize;
            var bmp = new Bitmap(size.Width, size.Height);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.White);
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            float k = Math.Min((float)size.Width / src.Width, (float)size.Height / src.Height);
            float w = src.Width * k, h = src.Height * k;
            g.DrawImage(src, (size.Width - w) / 2, (size.Height - h) / 2, w, h);
            g.DrawRectangle(Pens.Gray, 0, 0, size.Width - 1, size.Height - 1);
            return bmp;
        }

        private void ShowPreview() => _preview.Image = Sel >= 0 ? _pages[Sel].Image : null;

        private void MoveSelected(int delta)
        {
            int i = Sel, j = i + delta;
            if (i < 0 || j < 0 || j >= _pages.Count) return;
            (_pages[i], _pages[j]) = (_pages[j], _pages[i]);
            RefreshList(j);
        }

        private void RotateSelected()
        {
            int i = Sel;
            if (i < 0) return;
            _preview.Image = null;
            _pages[i].Image.RotateFlip(RotateFlipType.Rotate90FlipNone);
            RefreshList(i);
        }

        private void DeleteSelected()
        {
            int i = Sel;
            if (i < 0) return;
            _preview.Image = null;
            _pages[i].Dispose();
            _pages.RemoveAt(i);
            RefreshList(Math.Min(i, _pages.Count - 1));
            SetStatus($"Buferyje puslapių: {_pages.Count}");
        }

        private void ClearAll()
        {
            if (_pages.Count == 0) return;
            if (MessageBox.Show(this, "Ištrinti visus puslapius iš buferio?", "Patvirtinimas",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _preview.Image = null;
            foreach (var p in _pages) p.Dispose();
            _pages.Clear();
            RefreshList(-1);
            SetStatus("Buferis išvalytas.");
        }

        // ---------- išvestis ----------

        private void SavePdf()
        {
            if (_pages.Count == 0) { MessageBox.Show(this, "Buferis tuščias.", "PDF"); return; }
            using var dlg = new SaveFileDialog
            {
                Filter = "PDF dokumentas|*.pdf",
                FileName = $"Skenavimas_{DateTime.Now:yyyy-MM-dd_HHmm}.pdf"
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            RunBusy(() =>
            {
                PdfWriter.Save(dlg.FileName, _pages, (long)_numQuality.Value);
                SetStatus($"Išsaugota: {dlg.FileName} ({_pages.Count} psl.)");
            });
        }

        private void PrintPages()
        {
            if (_pages.Count == 0) { MessageBox.Show(this, "Buferis tuščias.", "Spausdinimas"); return; }
            using var doc = new PrintDocument { DocumentName = "Skenavimas" };
            doc.DefaultPageSettings.Margins = new Margins(25, 25, 25, 25);
            int idx = 0;
            doc.BeginPrint += (_, _) => idx = 0;
            doc.PrintPage += (_, e) =>
            {
                var page = _pages[idx];
                var area = e.MarginBounds; // 1/100 colio vienetai
                float w = page.Image.Width / page.Dpi * 100f, h = page.Image.Height / page.Dpi * 100f;
                float k = Math.Min(1f, Math.Min(area.Width / w, area.Height / h)); // tikras dydis arba sutalpinti
                w *= k; h *= k;
                e.Graphics!.DrawImage(page.Image, area.Left + (area.Width - w) / 2, area.Top + (area.Height - h) / 2, w, h);
                idx++;
                e.HasMorePages = idx < _pages.Count;
            };
            using var dlg = new PrintDialog { Document = doc, UseEXDialog = true };
            if (dlg.ShowDialog(this) == DialogResult.OK) RunBusy(doc.Print);
        }

        // ---------- pagalbinės ----------

        private void RunBusy(Action action)
        {
            Cursor = Cursors.WaitCursor;
            try { action(); }
            catch (Exception ex)
            {
                ScanLog.Error("vartotojo veiksmas", ex);
                SetStatus("Klaida.");
                MessageBox.Show(this, WiaScanner.Describe(ex), "Klaida", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { Cursor = Cursors.Default; }
        }

        private void SetStatus(string text) { _status.Text = text; Application.DoEvents(); }

        private static Label Lbl(string t) => new() { Text = t, AutoSize = true, Margin = new Padding(3, 8, 3, 3) };

        private static Button Btn(string t, EventHandler h)
        {
            var b = new Button { Text = t, AutoSize = true };
            b.Click += h;
            return b;
        }
    }
}
