using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SpecimenFX17.Imaging
{
    public class SpectralCalculatorForm : Form
    {
        private readonly HyperspectralCube _cube;
        private readonly IReadOnlyList<SelectionShape> _selections;
        private readonly List<(double From, double To)> _excludedRanges = new();

        private ListBox _lstExcluded = null!;
        private NumericUpDown _nudExclFrom = null!;
        private NumericUpDown _nudExclTo = null!;
        private Label _lblExclInfo = null!;
        private RichTextBox _txtFormula = null!;
        private Label _lblPreview = null!;
        private Label _lblError = null!;
        private Button _btnCalc = null!;
        private Button _btnSave = null!;
        private ComboBox _cmbColormap = null!;
        private ProgressBar _progress = null!;
        private Label _lblStatus = null!;
        private ListBox _lstBands = null!;
        private TabControl _tabs = null!;
        private PictureBox _tabImage = null!;
        private PictureBox _tabHisto = null!;
        private PictureBox _tabProfileH = null!;
        private PictureBox _tabProfileV = null!;
        private RichTextBox _tabStats = null!;

        private record ZoomRange(float X0, float X1, float Y0, float Y1);
        private ZoomRange? _zoomHisto;
        private ZoomRange? _zoomPH;
        private ZoomRange? _zoomPV;

        private TrackBar _trkRow = null!;
        private TrackBar _trkCol = null!;
        private Label _lblRow = null!;
        private Label _lblCol = null!;

        private float[,]? _resultData;
        private Bitmap? _resultBitmap;

        // ── Fórmulas predefinidas (Derivadas añadidas aquí) ────────────────
        private static readonly (string Name, string Formula)[] Presets =
        {
            ("NDVI",        "(B{800} - B{680}) / (B{800} + B{680})"),
            ("EVI",         "2.5 * (B{800} - B{680}) / (B{800} + 6*B{680} - 7.5*B{450} + 1)"),
            ("Ratio R/G",   "B{680} / B{550}"),
            ("1ª Derivada", "(B[10] - B[8]) / 2"),     // Aproximación por dif. central
            ("2ª Derivada", "B[11] - 2*B[10] + B[9]"), // Aproximación dif. segunda
            ("Suma B1+B2",  "B[1] + B[2]"),
            ("Media geom.", "sqrt(B[1] * B[2])"),
            ("Log B1+1",    "log(B[1] + 1)"),
            ("Cuadrado B1", "pow(B[1], 2)"),
            ("Raíz B1",     "sqrt(B[1])"),
        };

        public SpectralCalculatorForm(HyperspectralCube cube, IReadOnlyList<SelectionShape>? selections = null)
        {
            _cube = cube;
            _selections = selections ?? Array.Empty<SelectionShape>();
            Text = "Calculadora Espectral — SpecimenFX17";
            Size = new Size(1150, 820);
            MinimumSize = new Size(900, 650);
            BackColor = Color.FromArgb(18, 18, 26);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9f);
            BuildUI();
        }

        private void BuildUI()
        {
            var leftPanel = new Panel { Dock = DockStyle.Left, Width = 240, BackColor = Color.FromArgb(22, 22, 34), Padding = new Padding(8), AutoScroll = true };
            BuildLeftPanel(leftPanel);

            var centerPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(18, 18, 26) };

            var formulaPanel = BuildFormulaPanel();
            var selBanner = BuildSelectionBanner();
            var statusPanel = BuildStatusPanel();
            var toolPanel = BuildToolPanel();
            _tabs = BuildTabs();

            centerPanel.Controls.Add(_tabs);
            centerPanel.Controls.Add(toolPanel);
            centerPanel.Controls.Add(statusPanel);
            centerPanel.Controls.Add(selBanner);
            centerPanel.Controls.Add(formulaPanel);

            Controls.Add(centerPanel);
            Controls.Add(leftPanel);

            UpdatePreview();
        }

        private void BuildLeftPanel(Panel p)
        {
            int cy = 4;
            AddLbl(p, "BANDAS DISPONIBLES", cy, bold: true, color: Color.FromArgb(100, 160, 220)); cy += 20;
            AddLbl(p, "Doble clic para insertar:", cy, size: 8); cy += 18;

            _lstBands = new ListBox { Location = new Point(6, cy), Size = new Size(210, 200), BackColor = Color.FromArgb(30, 30, 45), ForeColor = Color.FromArgb(200, 220, 255), Font = new Font("Consolas", 8f), BorderStyle = BorderStyle.FixedSingle };
            RefreshBandList();
            _lstBands.DoubleClick += (_, _) => { if (_lstBands.SelectedIndex < 0) return; double wl = _cube.Header.Wavelengths[_lstBands.SelectedIndex]; _txtFormula.SelectedText = $"B{{{wl:F1}}}"; _txtFormula.Focus(); };
            p.Controls.Add(_lstBands); cy += 208;

            AddLbl(p, "EXCLUIR LONGITUDES DE ONDA", cy, bold: true, color: Color.FromArgb(220, 140, 80)); cy += 20;

            AddLbl(p, "Desde (nm):", cy, size: 8); cy += 16;
            _nudExclFrom = new NumericUpDown { Location = new Point(6, cy), Width = 210, Height = 22, Minimum = (decimal)(_cube.Header.Wavelengths.Count > 0 ? _cube.Header.Wavelengths[0] : 0), Maximum = (decimal)(_cube.Header.Wavelengths.Count > 0 ? _cube.Header.Wavelengths[^1] : 9999), Value = (decimal)(_cube.Header.Wavelengths.Count > 0 ? _cube.Header.Wavelengths[0] : 0), DecimalPlaces = 1, Increment = 1m, BackColor = Color.FromArgb(36, 36, 52), ForeColor = Color.White };
            p.Controls.Add(_nudExclFrom); cy += 26;

            AddLbl(p, "Hasta (nm):", cy, size: 8); cy += 16;
            _nudExclTo = new NumericUpDown { Location = new Point(6, cy), Width = 210, Height = 22, Minimum = (decimal)(_cube.Header.Wavelengths.Count > 0 ? _cube.Header.Wavelengths[0] : 0), Maximum = (decimal)(_cube.Header.Wavelengths.Count > 0 ? _cube.Header.Wavelengths[^1] : 9999), Value = (decimal)(_cube.Header.Wavelengths.Count > 0 ? _cube.Header.Wavelengths[^1] : 9999), DecimalPlaces = 1, Increment = 1m, BackColor = Color.FromArgb(36, 36, 52), ForeColor = Color.White };
            p.Controls.Add(_nudExclTo); cy += 26;

            var btnAddExcl = new Button { Text = "+ Añadir", Location = new Point(6, cy), Width = 100, Height = 24, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(120, 60, 20), ForeColor = Color.FromArgb(255, 200, 150), Font = new Font("Segoe UI", 8f), Cursor = Cursors.Hand };
            btnAddExcl.FlatAppearance.BorderColor = Color.FromArgb(180, 100, 40);
            btnAddExcl.Click += (_, _) => { double f = (double)_nudExclFrom.Value; double t = (double)_nudExclTo.Value; if (f > t) (f, t) = (t, f); _excludedRanges.Add((f, t)); _lstExcluded.Items.Add($"{f:F1} – {t:F1} nm"); RefreshBandList(); UpdateExclInfo(); UpdatePreview(); };
            p.Controls.Add(btnAddExcl);

            var btnDelExcl = new Button { Text = "✕ Eliminar", Location = new Point(116, cy), Width = 100, Height = 24, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(65, 30, 30), ForeColor = Color.FromArgb(255, 150, 150), Font = new Font("Segoe UI", 8f), Cursor = Cursors.Hand };
            btnDelExcl.FlatAppearance.BorderColor = Color.FromArgb(120, 50, 50);
            btnDelExcl.Click += (_, _) => { int idx = _lstExcluded.SelectedIndex; if (idx < 0) return; _excludedRanges.RemoveAt(idx); _lstExcluded.Items.RemoveAt(idx); RefreshBandList(); UpdateExclInfo(); UpdatePreview(); };
            p.Controls.Add(btnDelExcl); cy += 28;

            _lstExcluded = new ListBox { Location = new Point(6, cy), Size = new Size(210, 68), BackColor = Color.FromArgb(28, 20, 20), ForeColor = Color.FromArgb(255, 190, 120), Font = new Font("Consolas", 7.5f), BorderStyle = BorderStyle.FixedSingle };
            p.Controls.Add(_lstExcluded); cy += 76;

            _lblExclInfo = new Label { Location = new Point(6, cy), Size = new Size(210, 32), ForeColor = Color.FromArgb(170, 130, 80), Font = new Font("Segoe UI", 7.5f, FontStyle.Italic), Text = "Sin exclusiones activas", AutoSize = false };
            p.Controls.Add(_lblExclInfo); cy += 36;

            AddLbl(p, "FÓRMULAS RÁPIDAS", cy, bold: true, color: Color.FromArgb(100, 160, 220)); cy += 20;
            foreach (var (name, formula) in Presets)
            {
                var btn = new Button { Text = name, Location = new Point(6, cy), Width = 210, Height = 26, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(36, 36, 56), ForeColor = Color.FromArgb(180, 200, 240), Font = new Font("Segoe UI", 8.5f), Cursor = Cursors.Hand, Tag = formula };
                btn.FlatAppearance.BorderColor = Color.FromArgb(55, 55, 80);
                btn.Click += (s, _) => { _txtFormula.Text = (string)((Button)s!).Tag!; };
                p.Controls.Add(btn); cy += 30;
            }
        }

        private void RefreshBandList()
        {
            if (_lstBands == null) return;
            int prevSel = _lstBands.SelectedIndex;
            _lstBands.Items.Clear();
            for (int i = 0; i < _cube.Header.Wavelengths.Count; i++)
            {
                double wl = _cube.Header.Wavelengths[i];
                string tag = IsBandExcluded(i) ? "  ✕" : "";
                _lstBands.Items.Add($"B[{i + 1}]  {wl:F1} nm{tag}");
            }
            if (prevSel >= 0 && prevSel < _lstBands.Items.Count) _lstBands.SelectedIndex = prevSel;
        }

        private void UpdateExclInfo()
        {
            int excluded = Enumerable.Range(0, _cube.Bands).Count(IsBandExcluded);
            _lblExclInfo.Text = _excludedRanges.Count == 0 ? "Sin exclusiones activas" : $"{excluded} de {_cube.Bands} bandas excluidas\n(devuelven 0 en la fórmula)";
        }

        private bool IsBandExcluded(int bandIndex)
        {
            if (_excludedRanges.Count == 0) return false;
            double wl = bandIndex >= 0 && bandIndex < _cube.Header.Wavelengths.Count ? _cube.Header.Wavelengths[bandIndex] : -1;
            return _excludedRanges.Any(r => wl >= r.From && wl <= r.To);
        }

        private bool[,] BuildSelectionMask()
        {
            var mask = new bool[_cube.Lines, _cube.Samples];
            bool hasSelection = _selections.Count > 0;
            if (!hasSelection) { for (int l = 0; l < _cube.Lines; l++) for (int s = 0; s < _cube.Samples; s++) mask[l, s] = true; return mask; }
            foreach (var sh in _selections) { var shMask = sh.GetMask(_cube.Lines, _cube.Samples); for (int l = 0; l < _cube.Lines; l++) for (int s = 0; s < _cube.Samples; s++) if (shMask[l, s]) mask[l, s] = true; }
            return mask;
        }

        private Panel BuildSelectionBanner()
        {
            bool hasSel = _selections.Count > 0;
            int selCount = _selections.Count;
            string msg; Color bannerColor;
            if (hasSel) { var parts = new List<string> { $"{selCount} selección(es): " + string.Join(", ", _selections.Select(s => s.LegendIcon + s.ShortLabel)) }; msg = $"  🎯  Modo selección activo:  {string.Join("  +  ", parts)}   —   solo se calculará sobre esta selección"; bannerColor = Color.FromArgb(28, 55, 28); }
            else { msg = $"  🌐  Imagen completa:  {_cube.Samples} × {_cube.Lines} px  ({(long)_cube.Samples * _cube.Lines:N0} píxeles)   —   selecciona píxeles/regiones en la ventana principal para limitar el cálculo"; bannerColor = Color.FromArgb(25, 30, 45); }
            var banner = new Panel { Dock = DockStyle.Top, Height = 22, BackColor = bannerColor };
            var lbl = new Label { Dock = DockStyle.Fill, Text = msg, ForeColor = hasSel ? Color.FromArgb(130, 230, 130) : Color.FromArgb(110, 130, 180), Font = new Font("Segoe UI", 8f, hasSel ? FontStyle.Bold : FontStyle.Regular), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(4, 0, 0, 0) };
            banner.Controls.Add(lbl); return banner;
        }

        private Panel BuildFormulaPanel()
        {
            var fp = new Panel { Dock = DockStyle.Top, Height = 130, BackColor = Color.FromArgb(22, 22, 34) };
            AddLbl(fp, "EXPRESIÓN MATEMÁTICA", 6, bold: true, color: Color.FromArgb(100, 160, 220));
            _txtFormula = new RichTextBox { Location = new Point(10, 24), Size = new Size(fp.Width - 140, 44), Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top, BackColor = Color.FromArgb(28, 28, 50), ForeColor = Color.FromArgb(190, 240, 190), Font = new Font("Consolas", 12f), BorderStyle = BorderStyle.FixedSingle, WordWrap = false, Multiline = false, Text = "(B{800} - B{680}) / (B{800} + B{680})" };
            _txtFormula.TextChanged += (_, _) => UpdatePreview(); fp.Controls.Add(_txtFormula);
            var syntaxLbl = new Label { Location = new Point(10, 73), Size = new Size(fp.Width - 140, 50), Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top, ForeColor = Color.FromArgb(100, 100, 140), Font = new Font("Consolas", 7.5f), Text = "B{λ} = banda más cercana a λ nm   •   B[n] = banda por índice\nOps: + - * / ^    sqrt  log  log2  log10  exp  abs\nsin  cos  tan  asin  acos  atan  min(a,b)  max(a,b)  pow(a,b)  PI  E" };
            fp.Controls.Add(syntaxLbl);
            _btnCalc = new Button { Text = "▶  Calcular", Location = new Point(fp.Width - 125, 24), Width = 115, Height = 28, Anchor = AnchorStyles.Right | AnchorStyles.Top, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(35, 110, 55), ForeColor = Color.White, Font = new Font("Segoe UI", 9f, FontStyle.Bold), Cursor = Cursors.Hand };
            _btnCalc.FlatAppearance.BorderColor = Color.FromArgb(55, 150, 75); _btnCalc.Click += BtnCalc_Click; fp.Controls.Add(_btnCalc);
            _btnSave = new Button { Text = "💾  Guardar imagen", Location = new Point(fp.Width - 125, 56), Width = 115, Height = 28, Anchor = AnchorStyles.Right | AnchorStyles.Top, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(35, 70, 115), ForeColor = Color.White, Cursor = Cursors.Hand, Enabled = false };
            _btnSave.FlatAppearance.BorderColor = Color.FromArgb(55, 100, 155); _btnSave.Click += BtnSave_Click; fp.Controls.Add(_btnSave);
            fp.Resize += (_, _) => { _txtFormula.Width = fp.Width - 140; syntaxLbl.Width = fp.Width - 140; _btnCalc.Location = new Point(fp.Width - 125, 24); _btnSave.Location = new Point(fp.Width - 125, 56); };
            return fp;
        }

        private Panel BuildStatusPanel()
        {
            var sp = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = Color.FromArgb(14, 14, 24) };
            _lblPreview = new Label { Dock = DockStyle.Left, Width = 500, ForeColor = Color.FromArgb(120, 210, 120), Font = new Font("Consolas", 8.5f), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0) };
            _lblError = new Label { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(255, 100, 100), Font = new Font("Consolas", 8.5f), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(4, 0, 0, 0) };
            sp.Controls.Add(_lblError); sp.Controls.Add(_lblPreview); return sp;
        }

        private Panel BuildToolPanel()
        {
            var tp = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color.FromArgb(20, 20, 32) };
            AddLbl(tp, "Paleta:", 7, size: 8.5f);
            _cmbColormap = new ComboBox { Location = new Point(48, 4), Width = 155, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(36, 36, 55), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            _cmbColormap.Items.AddRange(Enum.GetNames(typeof(BliColormap))); _cmbColormap.SelectedIndex = 0; tp.Controls.Add(_cmbColormap);
            _progress = new ProgressBar { Location = new Point(215, 7), Width = 190, Height = 16, Style = ProgressBarStyle.Continuous, Visible = false }; tp.Controls.Add(_progress);
            _lblStatus = new Label { Location = new Point(415, 5), Width = 600, Height = 20, ForeColor = Color.FromArgb(140, 150, 190), Font = new Font("Consolas", 8f), TextAlign = ContentAlignment.MiddleLeft }; tp.Controls.Add(_lblStatus);
            return tp;
        }

        private TabControl BuildTabs()
        {
            var tc = new TabControl { Dock = DockStyle.Fill, BackColor = Color.FromArgb(18, 18, 26), Padding = new Point(12, 4) };
            var pgImg = new TabPage("🖼  Imagen") { BackColor = Color.FromArgb(10, 10, 18) }; _tabImage = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(10, 10, 18) }; pgImg.Controls.Add(_tabImage); tc.TabPages.Add(pgImg);
            var pgHisto = new TabPage("📊  Histograma") { BackColor = Color.FromArgb(10, 10, 18) }; _tabHisto = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Normal, BackColor = Color.FromArgb(10, 10, 18) }; _tabHisto.Resize += (_, _) => { if (_resultData != null) DrawHistogram(); }; AttachZoom(_tabHisto, () => DrawHistogram(), () => { _zoomHisto = null; DrawHistogram(); }, z => _zoomHisto = z, () => _zoomHisto); pgHisto.Controls.Add(_tabHisto); tc.TabPages.Add(pgHisto);
            var pgPH = new TabPage("↔  Perfil fila") { BackColor = Color.FromArgb(10, 10, 18) }; var phContainer = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(10, 10, 18) }; var phBar = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = Color.FromArgb(18, 18, 28) }; AddLbl(phBar, "Fila:", 8, size: 8.5f); _trkRow = new TrackBar { Location = new Point(40, 4), Width = 400, Height = 24, Minimum = 0, Maximum = Math.Max(0, _cube.Lines - 1), Value = _cube.Lines / 2, TickStyle = TickStyle.None, BackColor = Color.FromArgb(18, 18, 28) }; _lblRow = new Label { Location = new Point(445, 8), Width = 120, Height = 18, ForeColor = Color.FromArgb(150, 200, 255), Font = new Font("Consolas", 8.5f), Text = $"y = {_cube.Lines / 2}" }; _trkRow.Scroll += (_, _) => { _lblRow.Text = $"y = {_trkRow.Value}"; if (_resultData != null) DrawProfileH(); }; phBar.Controls.Add(_trkRow); phBar.Controls.Add(_lblRow); _tabProfileH = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Normal, BackColor = Color.FromArgb(10, 10, 18) }; _tabProfileH.Resize += (_, _) => { if (_resultData != null) DrawProfileH(); }; AttachZoom(_tabProfileH, () => DrawProfileH(), () => { _zoomPH = null; DrawProfileH(); }, z => _zoomPH = z, () => _zoomPH); phContainer.Controls.Add(_tabProfileH); phContainer.Controls.Add(phBar); pgPH.Controls.Add(phContainer); tc.TabPages.Add(pgPH);
            var pgPV = new TabPage("↕  Perfil columna") { BackColor = Color.FromArgb(10, 10, 18) }; var pvContainer = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(10, 10, 18) }; var pvBar = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = Color.FromArgb(18, 18, 28) }; AddLbl(pvBar, "Columna:", 8, size: 8.5f); _trkCol = new TrackBar { Location = new Point(68, 4), Width = 400, Height = 24, Minimum = 0, Maximum = Math.Max(0, _cube.Samples - 1), Value = _cube.Samples / 2, TickStyle = TickStyle.None, BackColor = Color.FromArgb(18, 18, 28) }; _lblCol = new Label { Location = new Point(475, 8), Width = 120, Height = 18, ForeColor = Color.FromArgb(150, 200, 255), Font = new Font("Consolas", 8.5f), Text = $"x = {_cube.Samples / 2}" }; _trkCol.Scroll += (_, _) => { _lblCol.Text = $"x = {_trkCol.Value}"; if (_resultData != null) DrawProfileV(); }; pvBar.Controls.Add(_trkCol); pvBar.Controls.Add(_lblCol); _tabProfileV = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Normal, BackColor = Color.FromArgb(10, 10, 18) }; _tabProfileV.Resize += (_, _) => { if (_resultData != null) DrawProfileV(); }; AttachZoom(_tabProfileV, () => DrawProfileV(), () => { _zoomPV = null; DrawProfileV(); }, z => _zoomPV = z, () => _zoomPV); pvContainer.Controls.Add(_tabProfileV); pvContainer.Controls.Add(pvBar); pgPV.Controls.Add(pvContainer); tc.TabPages.Add(pgPV);
            var pgStats = new TabPage("📋  Estadísticas") { BackColor = Color.FromArgb(14, 14, 24) }; _tabStats = new RichTextBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(14, 14, 24), ForeColor = Color.FromArgb(200, 220, 200), Font = new Font("Consolas", 10f), ReadOnly = true, BorderStyle = BorderStyle.None }; pgStats.Controls.Add(_tabStats); tc.TabPages.Add(pgStats);
            return tc;
        }

        private void UpdatePreview()
        {
            string f = _txtFormula.Text.Trim();
            if (string.IsNullOrEmpty(f)) { _lblPreview.Text = ""; _lblError.Text = ""; return; }
            try
            {
                int cx, cy;
                if (_selections.Count > 0) { var mask0 = _selections[0].GetMask(_cube.Lines, _cube.Samples); cx = _cube.Samples / 2; cy = _cube.Lines / 2; bool found = false; for (int l = 0; l < _cube.Lines && !found; l++) for (int s2 = 0; s2 < _cube.Samples && !found; s2++) if (mask0[l, s2]) { cy = l; cx = s2; found = true; } }
                else { cx = _cube.Samples / 2; cy = _cube.Lines / 2; }
                double r2 = Evaluate(f, cx, cy);
                _lblPreview.Text = $"  Preview píxel ({cx},{cy}): {r2:G8}"; _lblError.Text = ""; _btnCalc.Enabled = true;
            }
            catch (Exception ex) { _lblPreview.Text = ""; _lblError.Text = $"  ⚠  {ex.Message}"; _btnCalc.Enabled = false; }
        }

        private async void BtnCalc_Click(object? s, EventArgs e)
        {
            string formula = _txtFormula.Text.Trim(); if (string.IsNullOrEmpty(formula)) return;
            _btnCalc.Enabled = false; _btnSave.Enabled = false; _progress.Visible = true; _progress.Value = 0; _lblStatus.Text = "Calculando…"; _resultData = null;
            float[,]? result = null; string? error = null;
            bool[,] mask = BuildSelectionMask(); bool hasSelection = _selections.Count > 0;

            await Task.Run(() => {
                try
                {
                    result = new float[_cube.Lines, _cube.Samples];
                    for (int line = 0; line < _cube.Lines; line++)
                    {
                        for (int col = 0; col < _cube.Samples; col++)
                        {
                            if (!mask[line, col]) { result[line, col] = float.NaN; continue; }
                            result[line, col] = (float)Evaluate(formula, col, line);
                        }
                        if (line % 8 == 0) { int pct = (line + 1) * 100 / _cube.Lines; Invoke(() => { _progress.Value = pct; _lblStatus.Text = $"Calculando… {pct} %"; }); }
                    }
                }
                catch (Exception ex) { error = ex.Message; }
            });

            _progress.Visible = false; _btnCalc.Enabled = true;
            if (error != null) { _lblError.Text = $"  ⚠  Error: {error}"; return; }
            _resultData = result!;
            var stats = CalcStats(_resultData);
            int exclBands = Enumerable.Range(0, _cube.Bands).Count(IsBandExcluded);
            string exclInfo = exclBands > 0 ? $"  │  {exclBands} bandas excluidas" : "";
            string selInfo = hasSelection ? $"  │  Selección: {stats.ValidCount:N0} px" : $"  │  Imagen completa";
            _lblStatus.Text = $"Listo  │  Mín: {stats.Min:G5}  Máx: {stats.Max:G5}  Media: {stats.Mean:G5}  σ: {stats.Std:G4}{selInfo}{exclInfo}";

            _resultBitmap?.Dispose(); _resultBitmap = RenderFloatArray(_resultData, (BliColormap)_cmbColormap.SelectedIndex, formula, stats); _tabImage.Image = _resultBitmap;
            DrawHistogram();
            _trkRow.Value = Math.Clamp(_cube.Lines / 2, _trkRow.Minimum, _trkRow.Maximum); _trkCol.Value = Math.Clamp(_cube.Samples / 2, _trkCol.Minimum, _trkCol.Maximum);
            _lblRow.Text = $"y = {_trkRow.Value}"; _lblCol.Text = $"x = {_trkCol.Value}";
            DrawProfileH(); DrawProfileV();
            FillStats(stats, formula, hasSelection, exclBands); _btnSave.Enabled = true;
        }

        private void BtnSave_Click(object? s, EventArgs e) { if (_resultBitmap == null) return; using var dlg = new SaveFileDialog { Filter = "PNG (*.png)|*.png|TIFF (*.tif)|*.tif", FileName = "resultado_espectral" }; if (dlg.ShowDialog() == DialogResult.OK) { var fmt = dlg.FileName.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) ? ImageFormat.Tiff : ImageFormat.Png; _resultBitmap.Save(dlg.FileName, fmt); } }

        private void DrawHistogram()
        {
            if (_resultData == null) return;
            int w = Math.Max(_tabHisto.Width, 400), h = Math.Max(_tabHisto.Height, 200); var zoom = _zoomHisto;
            const int bins = 256; var stats = CalcStats(_resultData); float range = stats.Max - stats.Min; if (range < 1e-10f) range = 1f;
            float fullXMin = stats.Min, fullXRange = stats.Max - stats.Min; if (fullXRange < 1e-10f) fullXRange = 1f;
            float histoXMin = zoom != null ? fullXMin + zoom.X0 * fullXRange : stats.Min; float histoXMax = zoom != null ? fullXMin + zoom.X1 * fullXRange : stats.Max;
            float histoRange = histoXMax - histoXMin; if (histoRange < 1e-10f) histoRange = 1f;
            int[] counts = new int[bins]; int total = 0;
            for (int l = 0; l < _cube.Lines; l++) for (int c = 0; c < _cube.Samples; c++) { float v = _resultData[l, c]; if (float.IsNaN(v) || float.IsInfinity(v) || v < histoXMin || v > histoXMax) continue; int bin = Math.Clamp((int)((v - histoXMin) / histoRange * (bins - 1)), 0, bins - 1); counts[bin]++; total++; }
            int maxCount = counts.Max(), histoYMax2 = zoom != null ? (int)(zoom.Y1 * maxCount) : maxCount, histoYMin2 = zoom != null ? (int)(zoom.Y0 * maxCount) : 0;
            var bmp = new Bitmap(w, h); using var g = Graphics.FromImage(bmp); g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit; g.Clear(Color.FromArgb(12, 12, 20));
            const int padL = 58, padR = 20, padT = 28, padB = 40; var plot = new Rectangle(padL, padT, w - padL - padR, h - padT - padB);
            DrawPlotGrid(g, plot);
            float binW = (float)plot.Width / bins, yRangeH = histoYMax2 - histoYMin2; if (yRangeH < 1) yRangeH = 1;
            for (int i = 0; i < bins; i++) { if (counts[i] == 0) continue; float t = (float)i / (bins - 1), normH = Math.Clamp((counts[i] - histoYMin2) / yRangeH, 0f, 1f), barH = normH * plot.Height, x = plot.Left + i * binW, y = plot.Bottom - barH; var (r2, g2, b2) = GetColor(t, (BliColormap)_cmbColormap.SelectedIndex); using var brush = new SolidBrush(Color.FromArgb(200, r2, g2, b2)); g.FillRectangle(brush, x, y, Math.Max(1, binW), barH); }
            if (stats.Mean >= histoXMin && stats.Mean <= histoXMax) { float meanX = plot.Left + (stats.Mean - histoXMin) / histoRange * plot.Width; using var mp = new Pen(Color.FromArgb(220, 255, 255, 80), 1.5f) { DashStyle = DashStyle.Dash }; g.DrawLine(mp, meanX, plot.Top, meanX, plot.Bottom); using var sf2 = new Font("Consolas", 7.5f); using var sb2 = new SolidBrush(Color.FromArgb(180, 255, 255, 80)); g.DrawString($"μ={stats.Mean:G4}", sf2, sb2, meanX + 3, plot.Top + 2); }
            float p2x = plot.Left + (stats.P2 - histoXMin) / histoRange * plot.Width, p98x = plot.Left + (stats.P98 - histoXMin) / histoRange * plot.Width;
            using (var pp = new Pen(Color.FromArgb(160, 180, 180, 255), 1f) { DashStyle = DashStyle.Dot }) { if (stats.P2 >= histoXMin && stats.P2 <= histoXMax) g.DrawLine(pp, p2x, plot.Top, p2x, plot.Bottom); if (stats.P98 >= histoXMin && stats.P98 <= histoXMax) g.DrawLine(pp, p98x, plot.Top, p98x, plot.Bottom); }
            DrawPlotAxesXY(g, plot, histoXMin, histoXMax, histoYMin2, histoYMax2, "Valor", "Frecuencia");
            using var tf = new Font("Segoe UI", 8.5f, FontStyle.Bold); using var tb = new SolidBrush(Color.FromArgb(170, 180, 215)); g.DrawString($"Histograma  ({bins} bins,  {total:N0} píxeles válidos)", tf, tb, padL, 6);
            _tabHisto.Image?.Dispose(); _tabHisto.Image = bmp;
        }

        private void DrawProfileH() { if (_resultData == null) return; int row = Math.Clamp(_trkRow.Value, 0, _cube.Lines - 1), w = Math.Max(_tabProfileH.Width, 400), h = Math.Max(_tabProfileH.Height, 150); float[] profile = new float[_cube.Samples]; for (int c = 0; c < _cube.Samples; c++) profile[c] = _resultData[row, c]; var bmp = DrawLinePlot(w, h, profile, $"Columna (x)   —   fila y = {row}", "Valor", $"Perfil horizontal  │  fila {row}", Color.Cyan, _zoomPH); _tabProfileH.Image?.Dispose(); _tabProfileH.Image = bmp; }
        private void DrawProfileV() { if (_resultData == null) return; int col = Math.Clamp(_trkCol.Value, 0, _cube.Samples - 1), w = Math.Max(_tabProfileV.Width, 400), h = Math.Max(_tabProfileV.Height, 150); float[] profile = new float[_cube.Lines]; for (int l = 0; l < _cube.Lines; l++) profile[l] = _resultData[l, col]; var bmp = DrawLinePlot(w, h, profile, $"Fila (y)   —   columna x = {col}", "Valor", $"Perfil vertical  │  columna {col}", Color.Orange, _zoomPV); _tabProfileV.Image?.Dispose(); _tabProfileV.Image = bmp; }

        private Bitmap DrawLinePlot(int w, int h, float[] values, string xLabel, string yLabel, string title, Color color, ZoomRange? zoom = null)
        {
            var bmp = new Bitmap(w, h); using var g = Graphics.FromImage(bmp); g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit; g.Clear(Color.FromArgb(12, 12, 20));
            const int padL = 58, padR = 20, padT = 28, padB = 40; var plot = new Rectangle(padL, padT, w - padL - padR, h - padT - padB); if (plot.Width < 20 || plot.Height < 20) return bmp;
            float fullVMin = values.Where(v => !float.IsNaN(v) && !float.IsInfinity(v)).DefaultIfEmpty(0).Min(), fullVMax = values.Where(v => !float.IsNaN(v) && !float.IsInfinity(v)).DefaultIfEmpty(1).Max(), margin = (fullVMax - fullVMin) * 0.05f; if (margin < 1e-10f) margin = 0.05f; fullVMin -= margin; fullVMax += margin;
            float fullXRange = values.Length - 1, fullYRange = fullVMax - fullVMin, xMin = zoom != null ? zoom.X0 * fullXRange : 0, xMax = zoom != null ? zoom.X1 * fullXRange : fullXRange, vMin = zoom != null ? fullVMin + zoom.Y0 * fullYRange : fullVMin, vMax = zoom != null ? fullVMin + zoom.Y1 * fullYRange : fullVMax, vRng = vMax - vMin; if (vRng < 1e-10f) vRng = 1f; float xRng = xMax - xMin; if (xRng < 1e-10f) xRng = 1f;
            int iStart = Math.Max(0, (int)Math.Floor(xMin)), iEnd = Math.Min(values.Length - 1, (int)Math.Ceiling(xMax));
            DrawPlotGrid(g, plot); var pts = new List<PointF>();
            for (int i = iStart; i <= iEnd; i++) { float px = plot.Left + (i - xMin) / xRng * plot.Width, v = float.IsNaN(values[i]) || float.IsInfinity(values[i]) ? vMin : values[i], py = plot.Bottom - (v - vMin) / vRng * plot.Height; pts.Add(new PointF(px, Math.Clamp(py, plot.Top - 5, plot.Bottom + 5))); }
            if (pts.Count >= 2) { var fillPts = new PointF[pts.Count + 2]; fillPts[0] = new PointF(pts[0].X, plot.Bottom); pts.CopyTo(fillPts, 1); fillPts[^1] = new PointF(pts[^1].X, plot.Bottom); using (var fb = new SolidBrush(Color.FromArgb(25, color.R, color.G, color.B))) g.FillPolygon(fb, fillPts); using (var sh = new Pen(Color.FromArgb(50, color.R, color.G, color.B), 3f)) g.DrawLines(sh, pts.ToArray()); using (var lp = new Pen(color, 1.6f) { LineJoin = LineJoin.Round }) g.DrawLines(lp, pts.ToArray()); }
            DrawPlotAxesXY(g, plot, xMin, xMax, vMin, vMax, xLabel, yLabel);
            using var tf = new Font("Segoe UI", 8.5f, FontStyle.Bold); using var tb = new SolidBrush(Color.FromArgb(170, 180, 215)); g.DrawString(title, tf, tb, padL, 6);
            if (zoom != null) { using var zf = new Font("Segoe UI", 7.5f, FontStyle.Bold); using var zb = new SolidBrush(Color.FromArgb(200, 255, 200, 80)); g.DrawString("🔍 Zoom activo — clic derecho para resetear", zf, zb, padL + 4, padT + 4); }
            return bmp;
        }

        private void FillStats(ResultStats s, string formula, bool hasSelection = false, int exclBands = 0)
        {
            _tabStats.Clear(); _tabStats.SelectionFont = new Font("Consolas", 10f, FontStyle.Bold); _tabStats.SelectionColor = Color.FromArgb(100, 180, 255); _tabStats.AppendText("═══════════════════════════════════════════\n  ESTADÍSTICAS DEL RESULTADO\n═══════════════════════════════════════════\n\n");
            _tabStats.SelectionFont = new Font("Consolas", 9f); _tabStats.SelectionColor = Color.FromArgb(170, 200, 170); _tabStats.AppendText($"  Fórmula  :  {formula}\n  Imagen   :  {_cube.Samples} × {_cube.Lines} px\n");
            if (hasSelection) { _tabStats.SelectionColor = Color.FromArgb(130, 230, 130); _tabStats.AppendText($"  Ámbito   :  SELECCIÓN ({_selections.Count} forma(s))\n"); _tabStats.SelectionColor = Color.FromArgb(170, 200, 170); }
            else { _tabStats.SelectionColor = Color.FromArgb(150, 160, 200); _tabStats.AppendText($"  Ámbito   :  Imagen completa\n"); _tabStats.SelectionColor = Color.FromArgb(170, 200, 170); }
            if (exclBands > 0) { _tabStats.SelectionColor = Color.FromArgb(255, 190, 100); _tabStats.AppendText($"  Excluidas:  {exclBands} de {_cube.Bands} bandas (devuelven 0)\n"); foreach (var (f, t) in _excludedRanges) { _tabStats.SelectionColor = Color.FromArgb(200, 150, 70); _tabStats.AppendText($"              • {f:F1} – {t:F1} nm\n"); } _tabStats.SelectionColor = Color.FromArgb(170, 200, 170); }
            _tabStats.AppendText($"  Válidos  :  {s.ValidCount:N0} píxeles\n\n");
            _tabStats.SelectionFont = new Font("Consolas", 10f, FontStyle.Bold); _tabStats.SelectionColor = Color.FromArgb(100, 180, 255); _tabStats.AppendText("  DISTRIBUCIÓN DE VALORES\n  ─────────────────────────────────────\n"); _tabStats.SelectionFont = new Font("Consolas", 10f); _tabStats.SelectionColor = Color.FromArgb(200, 220, 200);
            void Row(string label, string value) { _tabStats.SelectionColor = Color.FromArgb(140, 160, 200); _tabStats.AppendText($"  {label,-20}"); _tabStats.SelectionColor = Color.FromArgb(230, 240, 210); _tabStats.AppendText($"{value}\n"); }
            Row("Mínimo:", s.Min.ToString("G8")); Row("Máximo:", s.Max.ToString("G8")); Row("Rango:", (s.Max - s.Min).ToString("G8")); Row("Media (μ):", s.Mean.ToString("G8")); Row("Mediana:", s.Median.ToString("G8")); Row("Desv. típica:", s.Std.ToString("G8")); Row("Varianza:", (s.Std * s.Std).ToString("G8")); Row("Sesgo:", s.Skew.ToString("G6")); _tabStats.AppendText("\n");
            _tabStats.SelectionFont = new Font("Consolas", 10f, FontStyle.Bold); _tabStats.SelectionColor = Color.FromArgb(100, 180, 255); _tabStats.AppendText("  PERCENTILES\n  ─────────────────────────────────────\n"); _tabStats.SelectionFont = new Font("Consolas", 10f);
            Row("P1%:", s.P1.ToString("G8")); Row("P2%:", s.P2.ToString("G8")); Row("P5%:", s.P5.ToString("G8")); Row("P25%:", s.P25.ToString("G8")); Row("P50%:", s.P50.ToString("G8")); Row("P75%:", s.P75.ToString("G8")); Row("P95%:", s.P95.ToString("G8")); Row("P98%:", s.P98.ToString("G8")); Row("P99%:", s.P99.ToString("G8"));
        }

        private record ResultStats(float Min, float Max, float Mean, float Median, float Std, float Skew, float P1, float P2, float P5, float P25, float P50, float P75, float P95, float P98, float P99, int ValidCount);
        private ResultStats CalcStats(float[,] data)
        {
            var vals = new List<float>(_cube.Lines * _cube.Samples);
            for (int l = 0; l < _cube.Lines; l++) for (int c = 0; c < _cube.Samples; c++) { float v = data[l, c]; if (!float.IsNaN(v) && !float.IsInfinity(v)) vals.Add(v); }
            vals.Sort(); if (vals.Count == 0) return new ResultStats(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            float Pct(float p) => vals[Math.Clamp((int)(vals.Count * p / 100f), 0, vals.Count - 1)]; float mean = vals.Average(), median = Pct(50), std = (float)Math.Sqrt(vals.Average(v => (v - mean) * (v - mean))), skew = std < 1e-10f ? 0f : vals.Average(v => (float)Math.Pow((v - mean) / std, 3));
            return new ResultStats(Min: vals[0], Max: vals[^1], Mean: mean, Median: median, Std: std, Skew: skew, P1: Pct(1), P2: Pct(2), P5: Pct(5), P25: Pct(25), P50: Pct(50), P75: Pct(75), P95: Pct(95), P98: Pct(98), P99: Pct(99), ValidCount: vals.Count);
        }

        private Bitmap RenderFloatArray(float[,] data, BliColormap colormap, string formula, ResultStats stats)
        {
            int lines = _cube.Lines, samples = _cube.Samples; float lo = stats.P2, hi = stats.P98, range = hi - lo; if (range < 1e-10f) range = 1f;
            var bmp = new Bitmap(samples, lines, PixelFormat.Format24bppRgb); var bData = bmp.LockBits(new Rectangle(0, 0, samples, lines), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb); int stride = bData.Stride; var pixels = new byte[stride * lines];
            for (int l = 0; l < lines; l++) { int row = l * stride; for (int c2 = 0; c2 < samples; c2++) { float v = data[l, c2], t = float.IsNaN(v) || float.IsInfinity(v) ? 0f : Math.Clamp((v - lo) / range, 0f, 1f); var (r2, g2, b2) = GetColor(t, colormap); int o = row + c2 * 3; pixels[o] = b2; pixels[o + 1] = g2; pixels[o + 2] = r2; } }
            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bData.Scan0, pixels.Length); bmp.UnlockBits(bData);
            using var gfx = Graphics.FromImage(bmp); gfx.SmoothingMode = SmoothingMode.AntiAlias; int barH = Math.Min(120, lines - 30), barW = 14, bx = samples - barW - 8, by = 15;
            for (int i = 0; i < barH; i++) { float t = 1f - (float)i / barH; var (r2, gc2, b2) = GetColor(t, colormap); using var pen = new Pen(Color.FromArgb(r2, gc2, b2)); gfx.DrawLine(pen, bx, by + i, bx + barW, by + i); }
            using var brd = new Pen(Color.White, 1f); using var sf2 = new Font("Arial", 7f); using var wb = new SolidBrush(Color.White); gfx.DrawRectangle(brd, bx, by, barW, barH); gfx.DrawString(hi.ToString("G4"), sf2, wb, bx - 2, by - 1); gfx.DrawString(lo.ToString("G4"), sf2, wb, bx - 2, by + barH + 1);
            string lbl = formula.Length > 60 ? formula[..57] + "…" : formula; using var ff = new Font("Consolas", 8f, FontStyle.Bold); using var fg = new SolidBrush(Color.FromArgb(160, 0, 0, 0)); var fsz = gfx.MeasureString(lbl, ff); gfx.FillRectangle(fg, 5, lines - fsz.Height - 5, fsz.Width + 4, fsz.Height + 2); gfx.DrawString(lbl, ff, wb, 7, lines - fsz.Height - 4);
            return bmp;
        }

        private double Evaluate(string formula, int col, int line)
        {
            string expr = Regex.Replace(formula, @"B\{([0-9.]+)\}", m => { double wl = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture); int band = FindClosestBand(wl); if (IsBandExcluded(band)) return "0"; float v = _cube[band, line, col]; return ToNum(v); });
            expr = Regex.Replace(expr, @"B\[([0-9]+)\]", m => { int band = Math.Clamp(int.Parse(m.Groups[1].Value) - 1, 0, _cube.Bands - 1); if (IsBandExcluded(band)) return "0"; float v = _cube[band, line, col]; return ToNum(v); });
            expr = Regex.Replace(expr, @"\bPI\b", Math.PI.ToString("R", System.Globalization.CultureInfo.InvariantCulture)); expr = Regex.Replace(expr, @"\bE\b", Math.E.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            expr = EvalFunctions(expr); expr = EvalPower(expr); return Convert.ToDouble(new DataTable().Compute(expr, null));
        }

        private static string ToNum(float v) => float.IsNaN(v) || float.IsInfinity(v) ? "0" : v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        private static string EvalFunctions(string expr)
        {
            var f1 = new Dictionary<string, Func<double, double>>(StringComparer.OrdinalIgnoreCase) { ["sqrt"] = Math.Sqrt, ["log"] = Math.Log, ["log2"] = Math.Log2, ["log10"] = Math.Log10, ["exp"] = Math.Exp, ["abs"] = Math.Abs, ["sin"] = Math.Sin, ["cos"] = Math.Cos, ["tan"] = Math.Tan, ["asin"] = Math.Asin, ["acos"] = Math.Acos, ["atan"] = Math.Atan, ["round"] = Math.Round, ["floor"] = Math.Floor, ["ceil"] = Math.Ceiling };
            var f2 = new Dictionary<string, Func<double, double, double>>(StringComparer.OrdinalIgnoreCase) { ["min"] = Math.Min, ["max"] = Math.Max, ["pow"] = Math.Pow, ["atan2"] = Math.Atan2 };
            bool changed = true; int guard = 0;
            while (changed && guard++ < 50)
            {
                changed = false;
                foreach (var (name, fn) in f1) { int idx = FindFunc(expr, name); while (idx >= 0) { int close = FindClose(expr, idx + name.Length); if (close < 0) throw new Exception($"Paréntesis sin cerrar en {name}()"); string inner = expr.Substring(idx + name.Length + 1, close - idx - name.Length - 1); double res = fn(EvalSimple(EvalFunctions(inner))); expr = expr[..idx] + Fmt(res) + expr[(close + 1)..]; changed = true; idx = FindFunc(expr, name); } }
                foreach (var (name, fn) in f2) { int idx = FindFunc(expr, name); while (idx >= 0) { int close = FindClose(expr, idx + name.Length); if (close < 0) throw new Exception($"Paréntesis sin cerrar en {name}()"); string inner = expr.Substring(idx + name.Length + 1, close - idx - name.Length - 1); int comma = TopComma(inner); if (comma < 0) throw new Exception($"{name}() necesita dos argumentos"); double a = EvalSimple(EvalFunctions(inner[..comma])); double b = EvalSimple(EvalFunctions(inner[(comma + 1)..])); expr = expr[..idx] + Fmt(fn(a, b)) + expr[(close + 1)..]; changed = true; idx = FindFunc(expr, name); } }
            }
            return expr;
        }

        private static string EvalPower(string expr) => Regex.Replace(expr, @"([\d.eE+\-]+)\^([\d.eE+\-]+)", m => { double a = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), b = double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture); return Fmt(Math.Pow(a, b)); });
        private static double EvalSimple(string e) => Convert.ToDouble(new DataTable().Compute(e.Trim(), null));
        private static string Fmt(double v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        private static int FindFunc(string expr, string name) { int idx = 0; while (true) { int pos = expr.IndexOf(name, idx, StringComparison.OrdinalIgnoreCase); if (pos < 0) return -1; int after = pos + name.Length; if (after < expr.Length && expr[after] == '(' && (pos == 0 || !char.IsLetterOrDigit(expr[pos - 1]))) return pos; idx = pos + 1; } }
        private static int FindClose(string expr, int openPos) { int d = 0; for (int i = openPos; i < expr.Length; i++) { if (expr[i] == '(') d++; else if (expr[i] == ')') { d--; if (d == 0) return i; } } return -1; }
        private static int TopComma(string s) { int d = 0; for (int i = 0; i < s.Length; i++) { if (s[i] == '(') d++; else if (s[i] == ')') d--; else if (s[i] == ',' && d == 0) return i; } return -1; }
        private int FindClosestBand(double wl) { if (_cube.Header.Wavelengths.Count == 0) return 0; return _cube.Header.Wavelengths.Select((w, i) => (diff: Math.Abs(w - wl), i)).OrderBy(x => x.diff).First().i; }
        private static void DrawPlotGrid(Graphics g, Rectangle plot) { using var gp = new Pen(Color.FromArgb(28, 255, 255, 255), 1f) { DashStyle = DashStyle.Dot }; for (int i = 0; i <= 5; i++) g.DrawLine(gp, plot.Left, plot.Bottom - (float)i / 5 * plot.Height, plot.Right, plot.Bottom - (float)i / 5 * plot.Height); for (int i = 0; i <= 6; i++) g.DrawLine(gp, plot.Left + (float)i / 6 * plot.Width, plot.Top, plot.Left + (float)i / 6 * plot.Width, plot.Bottom); using var bp = new Pen(Color.FromArgb(60, 255, 255, 255)); g.DrawRectangle(bp, plot); }
        private static void DrawPlotAxesXY(Graphics g, Rectangle plot, float xMin, float xMax, float yMin, float yMax, string xLabel, string yLabel) { using var tf = new Font("Consolas", 7.5f); using var tb = new SolidBrush(Color.FromArgb(155, 155, 195)); using var af = new Font("Segoe UI", 8f, FontStyle.Italic); using var ab = new SolidBrush(Color.FromArgb(115, 125, 165)); float xRng = xMax - xMin; if (xRng < 1e-10f) xRng = 1f; float yRng = yMax - yMin; if (yRng < 1e-10f) yRng = 1f; for (int i = 0; i <= 6; i++) { float v = xMin + xRng * i / 6; float x = plot.Left + (float)i / 6 * plot.Width; string lb = v >= 10000 || (Math.Abs(v) < 0.001f && v != 0) ? v.ToString("0.0e0") : v.ToString("G4"); var sz = g.MeasureString(lb, tf); g.DrawString(lb, tf, tb, x - sz.Width / 2, plot.Bottom + 2); } var xts = g.MeasureString(xLabel, af); g.DrawString(xLabel, af, ab, plot.Left + plot.Width / 2f - xts.Width / 2f, plot.Bottom + 20); for (int i = 0; i <= 5; i++) { float v = yMin + yRng * i / 5; float y = plot.Bottom - (float)i / 5 * plot.Height; string lb = v >= 10000 || (Math.Abs(v) < 0.001f && v != 0) ? v.ToString("0.0e0") : v.ToString("G4"); var sz = g.MeasureString(lb, tf); g.DrawString(lb, tf, tb, plot.Left - sz.Width - 3, y - sz.Height / 2); } var st = g.Save(); g.TranslateTransform(10, plot.Top + plot.Height / 2f); g.RotateTransform(-90); g.DrawString(yLabel, af, ab, -40, -6); g.Restore(st); }
        private static (byte R, byte G, byte B) GetColor(float t, BliColormap map) { float r, g, b; switch (map) { case BliColormap.HeatMap: return (ToByte(Math.Clamp(t * 3f, 0, 1)), ToByte(Math.Clamp(t * 3f - 1f, 0, 1)), ToByte(Math.Clamp(t * 3f - 2f, 0, 1))); case BliColormap.Grayscale: return (ToByte(t), ToByte(t), ToByte(t)); case BliColormap.ColdBlue: return (ToByte(Math.Clamp(t * 2 - 1, 0, 1)), ToByte(Math.Clamp(t * 2 - 1, 0, 1)), ToByte(Math.Clamp(t * 2, 0, 1))); case BliColormap.GreenFluorescent: return (ToByte(Math.Clamp(t * 2 - 1, 0, 1) * 0.5f), ToByte(Math.Clamp(t * 1.5f, 0, 1)), ToByte(Math.Clamp(t * 0.5f, 0, 1))); case BliColormap.RedFluorescent: return (ToByte(Math.Clamp(t * 1.5f, 0, 1)), ToByte(Math.Clamp(t * 0.5f, 0, 1) * 0.3f), 0); default: if (t < 0.125f) { r = 0; g = 0; b = 0.5f + t * 4f; } else if (t < 0.375f) { r = 0; g = (t - .125f) * 4f; b = 1f; } else if (t < 0.625f) { r = (t - .375f) * 4f; g = 1f; b = 1f - (t - .375f) * 4f; } else if (t < 0.875f) { r = 1f; g = 1f - (t - .625f) * 4f; b = 0f; } else { r = 1f; g = (t - .875f) * 8f; b = (t - .875f) * 8f; } return (ToByte(r), ToByte(g), ToByte(b)); } }
        private static byte ToByte(float v) => (byte)(Math.Clamp(v, 0f, 1f) * 255f);

        private void AttachZoom(PictureBox pb, Action redraw, Action reset, Action<ZoomRange?> setZoom, Func<ZoomRange?> getZoom)
        {
            const int padL = 58, padR = 20, padT = 28, padB = 40; Point dragStart = default; Point dragCur = default; bool dragging = false;
            pb.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { dragStart = e.Location; dragCur = e.Location; dragging = true; } else if (e.Button == MouseButtons.Right) reset(); };
            pb.MouseMove += (s, e) => { if (!dragging) return; dragCur = e.Location; pb.Invalidate(); };
            pb.MouseUp += (s, e) => { if (!dragging || e.Button != MouseButtons.Left) return; dragging = false; pb.Invalidate(); int dx = Math.Abs(e.X - dragStart.X); int dy = Math.Abs(e.Y - dragStart.Y); if (dx < 6 && dy < 6) return; int plotLeft = padL, plotRight = pb.Width - padR, plotTop = padT, plotBottom = pb.Height - padB, plotW = plotRight - plotLeft, plotH = plotBottom - plotTop; if (plotW < 1 || plotH < 1) return; float nx0 = Math.Clamp((float)(Math.Min(dragStart.X, e.X) - plotLeft) / plotW, 0f, 1f), nx1 = Math.Clamp((float)(Math.Max(dragStart.X, e.X) - plotLeft) / plotW, 0f, 1f), ny1 = Math.Clamp(1f - (float)(Math.Min(dragStart.Y, e.Y) - plotTop) / plotH, 0f, 1f), ny0 = Math.Clamp(1f - (float)(Math.Max(dragStart.Y, e.Y) - plotTop) / plotH, 0f, 1f); if (nx1 - nx0 < 0.01f || ny1 - ny0 < 0.01f) return; setZoom(new ZoomRange(nx0, nx1, ny0, ny1)); redraw(); };
            pb.Paint += (s, e) => { if (!dragging) return; int x1 = Math.Min(dragStart.X, dragCur.X), y1 = Math.Min(dragStart.Y, dragCur.Y), rw = Math.Abs(dragCur.X - dragStart.X), rh = Math.Abs(dragCur.Y - dragStart.Y); if (rw < 3 || rh < 3) return; using var fill = new SolidBrush(Color.FromArgb(40, 80, 200, 255)); e.Graphics.FillRectangle(fill, x1, y1, rw, rh); using var pen = new Pen(Color.FromArgb(200, 80, 200, 255), 1.5f) { DashStyle = DashStyle.Dash }; e.Graphics.DrawRectangle(pen, x1, y1, rw, rh); int c = 7; using var cp = new Pen(Color.FromArgb(220, 120, 220, 255), 2f); e.Graphics.DrawLine(cp, x1, y1, x1 + c, y1); e.Graphics.DrawLine(cp, x1, y1, x1, y1 + c); e.Graphics.DrawLine(cp, x1 + rw, y1, x1 + rw - c, y1); e.Graphics.DrawLine(cp, x1 + rw, y1, x1 + rw, y1 + c); e.Graphics.DrawLine(cp, x1, y1 + rh, x1 + c, y1 + rh); e.Graphics.DrawLine(cp, x1, y1 + rh, x1, y1 + rh - c); e.Graphics.DrawLine(cp, x1 + rw, y1 + rh, x1 + rw - c, y1 + rh); e.Graphics.DrawLine(cp, x1 + rw, y1 + rh, x1 + rw, y1 + rh - c); };
        }

        private static void AddLbl(Control p, string text, int cy, bool bold = false, Color? color = null, float size = 8.5f)
        {
            p.Controls.Add(new Label { Text = text, Location = new Point(8, cy), Width = 200, Height = 18, ForeColor = color ?? Color.FromArgb(130, 130, 175), Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), AutoSize = false });
        }
    }
}