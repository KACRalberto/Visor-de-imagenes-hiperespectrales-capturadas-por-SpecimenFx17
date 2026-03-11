using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SpecimenFX17.Imaging
{
    public class MainForm : Form
    {
        private HyperspectralCube? _originalCube;
        private HyperspectralCube? _baseCube;
        private HyperspectralCube? _cube;
        private HyperspectralCube? _whiteCube;
        private HyperspectralCube? _darkCube;
        private string _loadedFileName = "";

        private int _currentBand = 0;
        private bool _grayscaleMode = false;
        private bool _rgbMode = false;
        private Bitmap? _currentBitmap;
        private GraphicalInfoForm? _graphicalInfoForm;

        private readonly List<SelectionShape> _selections = new();
        private Point? _hoverImgPt = null;

        private static readonly Color[] SelColors =
        {
            Color.Cyan, Color.Yellow, Color.LimeGreen, Color.OrangeRed,
            Color.Magenta, Color.White, Color.DeepSkyBlue, Color.GreenYellow
        };

        private SelectionTool _tool = SelectionTool.Rectangle;
        private Button[] _toolBtns = null!;
        private Label _lblTip = null!;

        private bool _isDragging;
        private Point _dragStartScr, _dragStartImg, _dragCurScr;
        private bool _polyActive;
        private List<Point> _polyImg = new(), _polyScr = new();
        private Point _polyMouse;
        private List<Point> _freeImg = new(), _freeScr = new();

        private PictureBox _pictureBox = null!;
        private PictureBox _specPlot = null!;
        private Label _lblCoords = null!;
        private Label _lblSpecInfo = null!;
        private Label _lblBandInfo = null!;
        private RichTextBox _txtAnalysisReport = null!;

        private ComboBox _cmbBands = null!;
        private TrackBar _slider = null!;
        private CheckBox _chkAnalyze = null!;

        private ComboBox _cmbCmap = null!;
        private CheckBox _chkCbar = null!;
        private CheckBox _chkGray = null!;
        private CheckBox _chkRgb = null!;
        private NumericUpDown _nudGamma = null!;
        private NumericUpDown _nudLo = null!;
        private NumericUpDown _nudHi = null!;
        private NumericUpDown _nudThr = null!;
        private NumericUpDown _nudAutoTol = null!;

        private Button _btnLoad = null!;
        private Label _lblWhite = null!;
        private Label _lblDark = null!;
        private Button _btnCalibrate = null!;

        // ── Estado del pipeline quimiométrico (siempre se reconstruye desde _originalCube) ──
        private bool _stepNormalize = false;
        private bool _stepAbsorbance = false;
        private enum ScatterCorrection { None, SNV, MSC }
        private ScatterCorrection _stepScatter = ScatterCorrection.None;
        private bool _stepSG = false;
        private int _sgWindow = 15, _sgPoly = 2, _sgDeriv = 1;
        private bool _stepMedian = false;
        private Label _lblPipeline = null!;

        private Button _btnExport = null!;
        private Button _btnExpAll = null!;
        private Button _btnClear = null!;
        private ProgressBar _pb = null!;
        private StatusStrip _ss = null!;
        private ToolStripStatusLabel _slbl = null!;

        public MainForm()
        {
            Text = "SpecimenFX17 — Visor BLI Hiperespectral";
            Size = new Size(1400, 950); MinimumSize = new Size(1000, 700);
            BackColor = Color.FromArgb(18, 18, 26); ForeColor = Color.White;
            Font = new Font("Segoe UI", 9f);
            BuildUI();
        }

        private void BuildUI()
        {
            _ss = new StatusStrip { BackColor = Color.FromArgb(12, 12, 20), SizingGrip = false };
            _slbl = new ToolStripStatusLabel("Carga un archivo .hdr")
            { ForeColor = Color.FromArgb(100, 200, 100), Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            _pb = new ProgressBar { Style = ProgressBarStyle.Continuous, Visible = false, Size = new Size(200, 14) };
            _ss.Items.Add(_slbl); _ss.Items.Add(new ToolStripControlHost(_pb));

            var rp = new Panel
            {
                Dock = DockStyle.Right,
                Width = 280,
                BackColor = Color.FromArgb(24, 24, 36),
                AutoScroll = true,
                Padding = new Padding(10, 8, 8, 6)
            };
            BuildRightPanel(rp);

            var cp = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };

            var slPan = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Color.FromArgb(22, 22, 34) };

            var cmbContainer = new Panel { Dock = DockStyle.Left, Width = 260, Padding = new Padding(8, 10, 8, 10) };
            _cmbBands = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(38, 38, 55),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Consolas", 10f)
            };
            _cmbBands.SelectedIndexChanged += (_, _) => {
                if (_cmbBands.SelectedIndex >= 0 && _slider.Value != _cmbBands.SelectedIndex)
                {
                    _slider.Value = _cmbBands.SelectedIndex;
                    _currentBand = _slider.Value;
                    if (_graphicalInfoForm != null && !_graphicalInfoForm.IsDisposed) _graphicalInfoForm.CurrentBand = _currentBand;
                    RefreshDisplay();
                }
            };
            cmbContainer.Controls.Add(_cmbBands);

            _slider = new TrackBar
            {
                Dock = DockStyle.Fill,
                Minimum = 0,
                Maximum = 0,
                TickStyle = TickStyle.None,
                BackColor = Color.FromArgb(22, 22, 34)
            };
            _slider.Scroll += (_, _) => {
                _currentBand = _slider.Value;
                if (_cmbBands.Items.Count > _slider.Value) _cmbBands.SelectedIndex = _slider.Value;
                if (_graphicalInfoForm != null && !_graphicalInfoForm.IsDisposed) _graphicalInfoForm.CurrentBand = _currentBand;
                RefreshDisplay();
            };

            slPan.Controls.Add(_slider);
            slPan.Controls.Add(cmbContainer);

            var spCon = new Panel { Dock = DockStyle.Bottom, Height = 200, BackColor = Color.FromArgb(12, 12, 20) };
            _lblSpecInfo = new Label
            {
                Dock = DockStyle.Top,
                Height = 20,
                BackColor = Color.FromArgb(20, 20, 32),
                ForeColor = Color.FromArgb(140, 140, 190),
                Font = new Font("Segoe UI", 8f, FontStyle.Italic),
                Text = "  Rastreo activo con el ratón  •  Clic Izq = fijar selección  •  Clic Der = metadatos (°Brix)  •  Doble clic = limpiar",
                TextAlign = ContentAlignment.MiddleLeft
            };
            _specPlot = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(12, 12, 20), SizeMode = PictureBoxSizeMode.Normal };
            _specPlot.Resize += (_, _) => RedrawSpectrumPlot();
            spCon.Controls.Add(_specPlot); spCon.Controls.Add(_lblSpecInfo);

            var div = new Panel { Dock = DockStyle.Bottom, Height = 3, BackColor = Color.FromArgb(50, 50, 70) };

            _pictureBox = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black, Cursor = Cursors.Cross };
            _pictureBox.MouseMove += Pic_Move;
            _pictureBox.MouseDown += Pic_Down;
            _pictureBox.MouseUp += Pic_Up;
            _pictureBox.Paint += Pic_Paint;
            _pictureBox.MouseDoubleClick += Pic_DblClick;
            _pictureBox.MouseLeave += Pic_Leave;

            _lblCoords = new Label
            {
                AutoSize = false,
                Size = new Size(360, 20),
                Location = new Point(6, 6),
                BackColor = Color.FromArgb(160, 0, 0, 0),
                ForeColor = Color.FromArgb(200, 255, 200),
                Font = new Font("Consolas", 8f),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(4, 0, 0, 0)
            };
            _pictureBox.Controls.Add(_lblCoords);

            cp.Controls.Add(_pictureBox);
            cp.Controls.Add(div);
            cp.Controls.Add(spCon);
            cp.Controls.Add(slPan);

            Controls.Add(cp);
            Controls.Add(rp);
            Controls.Add(_ss);
        }

        private void BuildRightPanel(Panel p)
        {
            int cy = 8;
            _btnLoad = Btn(p, "📂  Cargar Cubo .hdr/.raw", ref cy, Color.FromArgb(40, 90, 140));
            _btnLoad.Click += BtnLoad_Click;

            var btnReset = Btn(p, "🔄  Restaurar Original", ref cy, Color.FromArgb(120, 50, 50));
            btnReset.Click += (s, e) => {
                if (_originalCube == null) return;
                _baseCube = _originalCube.Clone();
                _cube = _baseCube;
                // Resetear estado del pipeline
                _stepNormalize = false; _stepAbsorbance = false;
                _stepScatter = ScatterCorrection.None;
                _stepSG = false; _stepMedian = false;
                _chkAnalyze.Checked = false;
                _txtAnalysisReport.Text = "";
                PopulateBandsCombo();
                _slider.Maximum = _cube.Bands - 1;
                _currentBand = 0; _slider.Value = 0; _cmbBands.SelectedIndex = 0;
                RefreshDisplay(); ClearSpectrumPlot();
                UpdatePipelineLabel();
                _slbl.Text = "Imagen restaurada al estado original crudo.";
            };

            Sep(p, ref cy); Sec(p, "CALIBRACIÓN (Ref. B/N)", ref cy);

            var btnLoadWhite = Btn(p, "⚪ Cargar Ref. Blanca", ref cy, Color.FromArgb(60, 60, 60));
            _lblWhite = new Label { Location = new Point(8, cy), Width = 250, Height = 14, ForeColor = Color.Gray, Font = new Font("Segoe UI", 7f), Text = "Sin cargar" };
            p.Controls.Add(_lblWhite); cy += 18;

            var btnLoadDark = Btn(p, "⚫ Cargar Ref. Oscura", ref cy, Color.FromArgb(25, 25, 25));
            _lblDark = new Label { Location = new Point(8, cy), Width = 250, Height = 14, ForeColor = Color.Gray, Font = new Font("Segoe UI", 7f), Text = "Sin cargar" };
            p.Controls.Add(_lblDark); cy += 18;

            _btnCalibrate = Btn(p, "✨ Normalizar Imagen", ref cy, Color.FromArgb(120, 80, 40));
            _btnCalibrate.Enabled = false;

            var btnAbsorbance = Btn(p, "🧪 Convertir a Absorbancia", ref cy, Color.FromArgb(100, 40, 80));
            btnAbsorbance.Enabled = false;

            btnLoadWhite.Click += async (s, e) => {
                using var dlg = new OpenFileDialog { Filter = "ENVI Header (*.hdr)|*.hdr|Todos|*.*" };
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    _slbl.Text = "Cargando referencia blanca...";
                    try { _whiteCube = await Task.Run(() => HyperspectralCube.Load(dlg.FileName)); _lblWhite.Text = Path.GetFileName(dlg.FileName); CheckCalibrationReady(); _slbl.Text = "Referencia blanca cargada."; }
                    catch (Exception ex) { MessageBox.Show(ex.Message, "Error"); _slbl.Text = "Error."; }
                }
            };
            btnLoadDark.Click += async (s, e) => {
                using var dlg = new OpenFileDialog { Filter = "ENVI Header (*.hdr)|*.hdr|Todos|*.*" };
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    _slbl.Text = "Cargando referencia oscura...";
                    try { _darkCube = await Task.Run(() => HyperspectralCube.Load(dlg.FileName)); _lblDark.Text = Path.GetFileName(dlg.FileName); CheckCalibrationReady(); _slbl.Text = "Referencia oscura cargada."; }
                    catch (Exception ex) { MessageBox.Show(ex.Message, "Error"); _slbl.Text = "Error."; }
                }
            };

            // ── Calibración: toggle. Activa/desactiva la normalización en el pipeline ──
            _btnCalibrate.Click += async (s, e) => {
                if (_originalCube == null || _whiteCube == null || _darkCube == null) return;
                _stepNormalize = !_stepNormalize;
                if (!_stepNormalize) { _stepAbsorbance = false; }   // Absorbancia requiere normalización
                btnAbsorbance.Enabled = _stepNormalize;
                UpdateToggleButton(_btnCalibrate, _stepNormalize, Color.FromArgb(120, 80, 40));
                UpdateToggleButton(btnAbsorbance, _stepAbsorbance, Color.FromArgb(100, 40, 80));
                await RebuildWorkingCube();
            };

            // ── Absorbancia: toggle. Solo activo si normalización está activa ──
            btnAbsorbance.Click += async (s, e) => {
                if (!_stepNormalize) return;
                _stepAbsorbance = !_stepAbsorbance;
                UpdateToggleButton(btnAbsorbance, _stepAbsorbance, Color.FromArgb(100, 40, 80));
                await RebuildWorkingCube();
            };

            // ── Sección preprocesamiento quimiométrico ──────────────────────────────
            Sep(p, ref cy); Sec(p, "PREPROCESAMIENTO QUIMIOMÉTRICO", ref cy);

            // Pipeline visual: muestra los pasos activos en orden
            _lblPipeline = new Label
            {
                Location = new Point(8, cy),
                Width = 245,
                Height = 28,
                AutoSize = false,
                ForeColor = Color.FromArgb(100, 200, 130),
                BackColor = Color.FromArgb(18, 32, 22),
                Font = new Font("Consolas", 7.5f),
                Text = "Pipeline: Original",
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(4, 0, 0, 0)
            };
            p.Controls.Add(_lblPipeline); cy += 32;

            Lbl(p, "⚠ SNV y MSC son mutuamente excluyentes:", ref cy);

            var btnSnv = Btn(p, "📈  SNV (activo: NO)", ref cy, Color.FromArgb(40, 80, 90));
            var btnMsc = Btn(p, "📉  MSC (activo: NO)", ref cy, Color.FromArgb(50, 60, 90));

            Lbl(p, "Savitzky-Golay (Vent | Ord | Deriv):", ref cy);
            var pnlSg = new FlowLayoutPanel { Location = new Point(8, cy), Width = 245, Height = 28 };
            var nudSgWin = new NumericUpDown { Width = 60, Minimum = 3, Maximum = 51, Value = 15, Increment = 2, BackColor = Color.FromArgb(36, 36, 52), ForeColor = Color.White };
            var nudSgPol = new NumericUpDown { Width = 60, Minimum = 1, Maximum = 5, Value = 2, BackColor = Color.FromArgb(36, 36, 52), ForeColor = Color.White };
            var nudSgDer = new NumericUpDown { Width = 60, Minimum = 0, Maximum = 2, Value = 1, BackColor = Color.FromArgb(36, 36, 52), ForeColor = Color.White };
            pnlSg.Controls.Add(nudSgWin); pnlSg.Controls.Add(nudSgPol); pnlSg.Controls.Add(nudSgDer);
            p.Controls.Add(pnlSg); cy += 32;
            var btnSg = Btn(p, "〰️  Savitzky-Golay (activo: NO)", ref cy, Color.FromArgb(90, 70, 50));

            // SNV: toggle. Si se activa, desactiva MSC ──────────────────────────────
            btnSnv.Click += async (s, e) => {
                if (_originalCube == null) return;
                _stepScatter = (_stepScatter == ScatterCorrection.SNV) ? ScatterCorrection.None : ScatterCorrection.SNV;
                UpdateToggleButton(btnSnv, _stepScatter == ScatterCorrection.SNV, Color.FromArgb(40, 80, 90));
                UpdateToggleButton(btnMsc, _stepScatter == ScatterCorrection.MSC, Color.FromArgb(50, 60, 90));
                btnSnv.Text = $"📈  SNV (activo: {(_stepScatter == ScatterCorrection.SNV ? "SÍ" : "NO")})";
                btnMsc.Text = $"📉  MSC (activo: {(_stepScatter == ScatterCorrection.MSC ? "SÍ" : "NO")})";
                await RebuildWorkingCube();
            };

            // MSC: toggle. Si se activa, desactiva SNV ──────────────────────────────
            btnMsc.Click += async (s, e) => {
                if (_originalCube == null) return;
                _stepScatter = (_stepScatter == ScatterCorrection.MSC) ? ScatterCorrection.None : ScatterCorrection.MSC;
                UpdateToggleButton(btnSnv, _stepScatter == ScatterCorrection.SNV, Color.FromArgb(40, 80, 90));
                UpdateToggleButton(btnMsc, _stepScatter == ScatterCorrection.MSC, Color.FromArgb(50, 60, 90));
                btnSnv.Text = $"📈  SNV (activo: {(_stepScatter == ScatterCorrection.SNV ? "SÍ" : "NO")})";
                btnMsc.Text = $"📉  MSC (activo: {(_stepScatter == ScatterCorrection.MSC ? "SÍ" : "NO")})";
                await RebuildWorkingCube();
            };

            // Savitzky-Golay: toggle con parámetros actuales ─────────────────────────
            btnSg.Click += async (s, e) => {
                if (_originalCube == null) return;
                _stepSG = !_stepSG;
                if (_stepSG) { _sgWindow = (int)nudSgWin.Value; _sgPoly = (int)nudSgPol.Value; _sgDeriv = (int)nudSgDer.Value; }
                UpdateToggleButton(btnSg, _stepSG, Color.FromArgb(90, 70, 50));
                btnSg.Text = $"〰️  Savitzky-Golay (activo: {(_stepSG ? "SÍ" : "NO")})";
                await RebuildWorkingCube();
            };

            // Al cambiar parámetros SG, si está activo, reconstruir automáticamente
            nudSgWin.ValueChanged += async (_, _) => { if (_stepSG) { _sgWindow = (int)nudSgWin.Value; await RebuildWorkingCube(); } };
            nudSgPol.ValueChanged += async (_, _) => { if (_stepSG) { _sgPoly = (int)nudSgPol.Value; await RebuildWorkingCube(); } };
            nudSgDer.ValueChanged += async (_, _) => { if (_stepSG) { _sgDeriv = (int)nudSgDer.Value; await RebuildWorkingCube(); } };

            Sep(p, ref cy); Sec(p, "HERRAMIENTA DE SELECCIÓN", ref cy);
            _lblTip = new Label { Location = new Point(8, cy), Width = 250, Height = 16, ForeColor = Color.FromArgb(120, 200, 120), Font = new Font("Segoe UI", 7f, FontStyle.Italic), Text = "Arrastra para seleccionar un rectángulo" };
            p.Controls.Add(_lblTip); cy += 20;

            var grid = new TableLayoutPanel { Location = new Point(8, cy), Width = 245, Height = 85, ColumnCount = 2, RowCount = 3, BackColor = Color.Transparent };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 33.3f)); grid.RowStyles.Add(new RowStyle(SizeType.Percent, 33.3f)); grid.RowStyles.Add(new RowStyle(SizeType.Percent, 33.3f));

            var defs = new (string Lbl, SelectionTool Mode, string Tip)[] {
                ("▭ Rectángulo", SelectionTool.Rectangle, "Arrastra para rectángulo"),
                ("⬟ Polígono",   SelectionTool.Polygon,   "Clic = vértice • Enter = cerrar"),
                ("○ Círculo",    SelectionTool.Circle,    "Arrastra desde el centro"),
                ("✏ Lasso",      SelectionTool.Freehand,  "Mantén pulsado y dibuja"),
                ("🪄 Auto ROI",  SelectionTool.AutoDetect,"Clic=Nuevo • Shift=Sumar • Alt=Restar")
            };

            _toolBtns = new Button[5];
            for (int i = 0; i < 5; i++)
            {
                var (lbl, mode, tip) = defs[i];
                var tb = new Button { Text = lbl, Dock = DockStyle.Fill, Margin = new Padding(2), FlatStyle = FlatStyle.Flat, BackColor = mode == _tool ? Color.FromArgb(50, 110, 170) : Color.FromArgb(32, 32, 48), ForeColor = Color.White, Font = new Font("Segoe UI", 7.5f), Cursor = Cursors.Hand, Tag = (mode, tip) };
                tb.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 100);
                tb.Click += (_, _) => { var (m, t) = ((SelectionTool, string))tb.Tag!; SetTool(m, t); };
                grid.Controls.Add(tb, i % 2, i / 2); _toolBtns[i] = tb;
            }
            p.Controls.Add(grid); cy += 90;

            Lbl(p, "Tolerancia SAM (%):", ref cy);
            _nudAutoTol = Num(p, ref cy, 10m, 1m, 100m, 1m, 0);

            // ------ BOTONES DE MORFOLOGÍA MEJORADOS ------
            var btnErode = Btn(p, "➖ Contraer Auto-ROI (Erosión)", ref cy, Color.FromArgb(80, 50, 50));
            var btnDilate = Btn(p, "➕ Expandir Auto-ROI (Dilatación)", ref cy, Color.FromArgb(50, 80, 50));
            var btnFill = Btn(p, "🕳️ Rellenar Huecos Internos", ref cy, Color.FromArgb(70, 60, 90));

            btnErode.Click += (_, _) => ApplyMorphologyToMasks("erode");
            btnDilate.Click += (_, _) => ApplyMorphologyToMasks("dilate");
            btnFill.Click += (_, _) => ApplyMorphologyToMasks("fill");

            Sep(p, ref cy); Sec(p, "DATOS Y SESIONES", ref cy);
            var btnSaveSes = Btn(p, "💾  Guardar Sesión", ref cy, Color.FromArgb(50, 80, 60));
            var btnLoadSes = Btn(p, "📂  Cargar Sesión", ref cy, Color.FromArgb(60, 60, 80));
            var btnDual = Btn(p, "⚖️  Comparador Multifichero", ref cy, Color.FromArgb(90, 60, 90));

            var btnBatch = Btn(p, "⚙️  Procesamiento por Lotes", ref cy, Color.FromArgb(140, 100, 40));
            btnBatch.Click += BtnBatch_Click;

            btnSaveSes.Click += (_, _) => {
                if (_cube == null || _selections.Count == 0) { MessageBox.Show("No hay datos para guardar."); return; }
                using var sfd = new SaveFileDialog { Filter = "Sesión JSON (*.json)|*.json" };
                if (sfd.ShowDialog() == DialogResult.OK) SessionManager.SaveSession(sfd.FileName, _selections);
            };
            btnLoadSes.Click += (_, _) => {
                if (_cube == null) { MessageBox.Show("Carga un cubo primero."); return; }
                using var ofd = new OpenFileDialog { Filter = "Sesión JSON (*.json)|*.json" };
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    var loaded = SessionManager.LoadSession(ofd.FileName);
                    _selections.Clear(); _selections.AddRange(loaded);
                    _btnClear.Enabled = true; RefreshDisplay();
                }
            };
            btnDual.Click += (_, _) => new DualViewerForm(_cube).Show();

            Sep(p, ref cy); Sec(p, "ANÁLISIS ESPECTRAL", ref cy);

            _chkAnalyze = Chk(p, "Analizar bandas (Media, Min/Max, PCA)", ref cy, false);
            _chkAnalyze.CheckedChanged += ChkAnalyze_CheckedChanged;

            var btnGraph = Btn(p, "📊  Ver información gráfica", ref cy, Color.FromArgb(60, 100, 140));
            btnGraph.Click += (_, _) => {
                if (_cube == null) return;
                if (_graphicalInfoForm == null || _graphicalInfoForm.IsDisposed)
                {
                    _graphicalInfoForm = new GraphicalInfoForm(_cube);
                    _graphicalInfoForm.Show();
                }
                else _graphicalInfoForm.BringToFront();
            };

            var btnCompare = Btn(p, "⚖️  Comparativa ROI (Orig vs Tratada)", ref cy, Color.FromArgb(100, 80, 140));
            btnCompare.Click += (s, e) => {
                if (_originalCube == null || _cube == null) return;
                if (_selections.Count == 0)
                {
                    MessageBox.Show("Selecciona al menos un ROI para comparar las curvas.", "Aviso"); return;
                }
                new RoiComparisonForm(_originalCube, _cube, _selections.ToList(), _currentBand).Show();
            };

            var bc = Btn(p, "🧮  Calculadora de Fórmulas", ref cy, Color.FromArgb(70, 45, 110));
            var ba = Btn(p, "🔬  Herramientas Avanzadas", ref cy, Color.FromArgb(140, 70, 45));
            var bp = Btn(p, "🍊  Predecir °Brix (PLS)", ref cy, Color.FromArgb(140, 90, 30));

            var b3d = Btn(p, "🧊  Visor de Hipercubo 3D", ref cy, Color.FromArgb(40, 110, 130));
            b3d.Click += (_, _) => { if (_cube != null) new Hypercube3DForm(_cube, _selections.AsReadOnly()).Show(); };

            bc.Click += (_, _) => { if (_cube != null) new SpectralCalculatorForm(_cube, _selections.AsReadOnly()).Show(); };
            ba.Click += (_, _) => { if (_cube != null) new AdvancedAnalysisForm(_cube, _selections.AsReadOnly()).Show(); };
            bp.Click += (_, _) => { if (_cube != null) new PlsPredictionForm(_cube, _selections.AsReadOnly()).Show(); };

            _txtAnalysisReport = new RichTextBox { Location = new Point(8, cy), Width = 245, Height = 100, ForeColor = Color.LightGray, BackColor = Color.FromArgb(20, 20, 28), Font = new Font("Consolas", 7f), ReadOnly = true };
            p.Controls.Add(_txtAnalysisReport); cy += 110;

            Sep(p, ref cy); Sec(p, "VISUALIZACIÓN", ref cy);
            Lbl(p, "Paleta de color:", ref cy);
            _cmbCmap = new ComboBox { Location = new Point(8, cy), Width = 245, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(38, 38, 55), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            _cmbCmap.Items.AddRange(Enum.GetNames(typeof(BliColormap)));
            _cmbCmap.SelectedIndex = 0;
            _cmbCmap.SelectedIndexChanged += (_, _) => RefreshDisplay();
            p.Controls.Add(_cmbCmap); cy += 28;

            _chkCbar = Chk(p, "Mostrar barra de escala", ref cy, true);
            _chkGray = Chk(p, "Modo escala de grises", ref cy, false);
            _chkRgb = Chk(p, "Modo RGB (Color real visible)", ref cy, false);

            _chkCbar.CheckedChanged += (_, _) => RefreshDisplay();
            _chkGray.CheckedChanged += (_, _) => {
                _grayscaleMode = _chkGray.Checked;
                if (_grayscaleMode) _chkRgb.Checked = false;
                _cmbCmap.Enabled = !_grayscaleMode && !_rgbMode; RefreshDisplay();
            };
            _chkRgb.CheckedChanged += (_, _) => {
                _rgbMode = _chkRgb.Checked;
                if (_rgbMode) _chkGray.Checked = false;
                _cmbCmap.Enabled = !_grayscaleMode && !_rgbMode; _slider.Enabled = !_rgbMode; _cmbBands.Enabled = !_rgbMode; RefreshDisplay();
            };

            Sep(p, ref cy); Sec(p, "AJUSTES DE IMAGEN", ref cy);
            Lbl(p, "Gamma (1 = lineal):", ref cy); _nudGamma = Num(p, ref cy, 1.0m, 0.1m, 5.0m, 0.1m, 1);

            Lbl(p, "Percentil bajo (%):", ref cy); _nudLo = Num(p, ref cy, 2m, 0m, 49m, 1m, 0);
            Lbl(p, "Percentil alto (%):", ref cy); _nudHi = Num(p, ref cy, 98m, 51m, 100m, 1m, 0);

            Lbl(p, "Umbral de señal:", ref cy); _nudThr = Num(p, ref cy, 0m, 0m, 9999999m, 1m, 0);
            foreach (var n in new[] { _nudGamma, _nudLo, _nudHi, _nudThr }) n.ValueChanged += (_, _) => RefreshDisplay();

            var btnMedian = Btn(p, "🌫️ Filtro Mediana 3x3 (activo: NO)", ref cy, Color.FromArgb(70, 90, 110));
            btnMedian.Click += async (s, e) => {
                if (_originalCube == null) return;
                _stepMedian = !_stepMedian;
                UpdateToggleButton(btnMedian, _stepMedian, Color.FromArgb(70, 90, 110));
                btnMedian.Text = $"🌫️ Filtro Mediana 3x3 (activo: {(_stepMedian ? "SÍ" : "NO")})";
                await RebuildWorkingCube();
            };

            Sep(p, ref cy); Sec(p, "EXPORTAR IMÁGENES", ref cy);
            _btnExport = Btn(p, "💾  Exportar vista actual", ref cy, Color.FromArgb(35, 95, 55));
            _btnExpAll = Btn(p, "📦  Exportar todas las bandas", ref cy, Color.FromArgb(30, 75, 45));
            _btnClear = Btn(p, "🗑️  Limpiar selecciones", ref cy, Color.FromArgb(110, 40, 40));
            _btnExport.Enabled = _btnExpAll.Enabled = _btnClear.Enabled = false;
            _btnExport.Click += BtnExport_Click;
            _btnExpAll.Click += BtnExportAll_Click;
            _btnClear.Click += (_, _) => ClearAll();

            Sep(p, ref cy); Sec(p, "INFO DE BANDA", ref cy);
            _lblBandInfo = new Label { Location = new Point(8, cy), Width = 245, Height = 110, ForeColor = Color.FromArgb(160, 160, 190), Font = new Font("Consolas", 7.5f), Text = "—" };
            p.Controls.Add(_lblBandInfo);
        }

        // ====== FUNCIÓN PARA APLICAR MORFOLOGÍA ======
        private void ApplyMorphologyToMasks(string operation)
        {
            bool changed = false;

            // SOLUCIÓN: Añadimos .ToList() para iterar sobre una copia segura y evitar el crash
            foreach (var sh in _selections.ToList())
            {
                if (sh is MaskShape mask)
                {
                    if (operation == "dilate") mask.Dilate(1);
                    else if (operation == "erode") mask.Erode(1);
                    else if (operation == "fill") mask.FillHoles();
                    changed = true;
                }
            }
            if (changed) RefreshDisplay();
        }

        private async void BtnBatch_Click(object? s, EventArgs e)
        {
            using var dlgFolder = new FolderBrowserDialog { Description = "Selecciona la carpeta que contiene tus archivos .hdr" };
            if (dlgFolder.ShowDialog() != DialogResult.OK) return;

            using var dlgSave = new SaveFileDialog { Filter = "Archivo CSV (*.csv)|*.csv", FileName = "Resultados_Lote.csv", Title = "Guardar Excel de resultados" };
            if (dlgSave.ShowDialog() != DialogResult.OK) return;

            var options = new BatchOptions { ApplySNV = true, ApplyMSC = false, ConvertToAbsorbance = true };

            _pb.Visible = true;
            _slbl.Text = "Procesando imágenes por lotes... Por favor, espera.";

            var progress = new Progress<int>(v => {
                _pb.Value = v;
                _slbl.Text = $"Procesando lote... {v}% completado";
            });

            try
            {
                await BatchProcessor.ProcessFolderAsync(dlgFolder.SelectedPath, dlgSave.FileName, options, progress);
                MessageBox.Show("¡Procesamiento por lotes completado!\nSe ha guardado el archivo CSV.", "Éxito", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ocurrió un error procesando el lote:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _pb.Visible = false;
                _slbl.Text = "Procesamiento por lotes finalizado.";
            }
        }

        private void PopulateBandsCombo()
        {
            _cmbBands.Items.Clear();
            if (_cube == null) return;

            int origBands = _baseCube != null ? _baseCube.Header.Bands : _cube.Header.Bands;

            for (int i = 0; i < origBands; i++)
            {
                double wl = _cube.Header.Wavelengths.Count > i ? _cube.Header.Wavelengths[i] : 0;
                _cmbBands.Items.Add($"Banda {i + 1} - {wl:F1} nm");
            }

            if (_cube.Bands > origBands)
            {
                _cmbBands.Items.Add("Media"); _cmbBands.Items.Add("Mínima");
                _cmbBands.Items.Add("Máxima"); _cmbBands.Items.Add("Rango");
                int numPca = _cube.Bands - origBands - 4;
                for (int i = 0; i < numPca; i++) _cmbBands.Items.Add($"PC {i + 1}");
            }

            if (_cmbBands.Items.Count > 0) _cmbBands.SelectedIndex = Math.Clamp(_currentBand, 0, _cmbBands.Items.Count - 1);
        }

        private async Task RunAnalysisAsync()
        {
            if (_baseCube == null) return;
            _slbl.Text = "Calculando análisis de bandas y PCA... (Esto puede tardar)";
            _pb.Visible = true; _pb.Style = ProgressBarStyle.Marquee;
            _chkAnalyze.Enabled = false;

            await Task.Run(() => {
                bool[,] mask = new bool[_baseCube.Lines, _baseCube.Samples];
                if (_selections.Count > 0)
                {
                    foreach (var sh in _selections)
                    {
                        var m = sh.GetMask(_baseCube.Lines, _baseCube.Samples);
                        for (int l = 0; l < _baseCube.Lines; l++)
                            for (int c = 0; c < _baseCube.Samples; c++)
                                if (m[l, c]) mask[l, c] = true;
                    }
                }
                else
                {
                    int xMin = _baseCube.Samples < 1000 ? 40 : 200;
                    int xMax = _baseCube.Samples - (_baseCube.Samples < 1000 ? 60 : 200);
                    int yMin = _baseCube.Lines < 600 ? 70 : 300;
                    int yMax = _baseCube.Lines - (_baseCube.Lines < 600 ? 40 : 100);

                    for (int l = 0; l < _baseCube.Lines; l++)
                    {
                        for (int s = 0; s < _baseCube.Samples; s++)
                        {
                            mask[l, s] = (l >= yMin && l <= yMax && s >= xMin && s <= xMax);
                        }
                    }
                }
                _cube = _baseCube.GenerateAnalyzedCube(10, mask);
            });

            _pb.Visible = false; _pb.Style = ProgressBarStyle.Continuous;
            _chkAnalyze.Enabled = true;
            _slbl.Text = "Análisis completado. Bandas sintéticas añadidas al menú.";

            if (_cube != null) _txtAnalysisReport.Text = _cube.AnalysisReport;

            PopulateBandsCombo();
            _slider.Maximum = _cube!.Bands - 1;
            if (_currentBand >= _cube.Bands) _currentBand = _cube.Bands - 1;
            _slider.Value = _currentBand;
            _cmbBands.SelectedIndex = _currentBand;
            RefreshDisplay();
        }

        private async void ChkAnalyze_CheckedChanged(object? s, EventArgs e)
        {
            if (_baseCube == null) return;
            if (_chkAnalyze.Checked)
            {
                await RunAnalysisAsync();
            }
            else
            {
                _cube = _baseCube;
                _slbl.Text = "Análisis desactivado. Restaurado cubo original.";
                _txtAnalysisReport.Text = "";
                PopulateBandsCombo();
                _slider.Maximum = _cube!.Bands - 1;
                if (_currentBand >= _cube.Bands) _currentBand = _cube.Bands - 1;
                _slider.Value = _currentBand;
                _cmbBands.SelectedIndex = _currentBand;
                RefreshDisplay();
            }
        }

        private Button Btn(Panel p, string t, ref int cy, Color bg)
        {
            var b = new Button { Text = t, Location = new Point(8, cy), Width = 245, Height = 35, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = bg, ForeColor = Color.White, Cursor = Cursors.Hand };
            b.FlatAppearance.BorderColor = Color.FromArgb(Math.Min(255, bg.R + 35), Math.Min(255, bg.G + 35), Math.Min(255, bg.B + 35));
            p.Controls.Add(b); cy += 40; return b;
        }
        private NumericUpDown Num(Panel p, ref int cy, decimal v, decimal mn, decimal mx, decimal inc, int dec)
        {
            var n = new NumericUpDown { Location = new Point(8, cy), Width = 245, Minimum = mn, Maximum = mx, Value = v, Increment = inc, DecimalPlaces = dec, BackColor = Color.FromArgb(36, 36, 52), ForeColor = Color.White };
            p.Controls.Add(n); cy += 26; return n;
        }
        private void Lbl(Panel p, string t, ref int cy) { p.Controls.Add(new Label { Text = t, Location = new Point(8, cy), Width = 245, Height = 16, ForeColor = Color.FromArgb(140, 140, 170), Font = new Font("Segoe UI", 8f) }); cy += 17; }
        private void Sec(Panel p, string t, ref int cy) { p.Controls.Add(new Label { Text = t, Location = new Point(8, cy), Width = 245, Height = 18, ForeColor = Color.FromArgb(100, 160, 220), Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }); cy += 20; }
        private CheckBox Chk(Panel p, string t, ref int cy, bool v) { var c = new CheckBox { Text = t, Location = new Point(8, cy), Width = 245, Checked = v, ForeColor = Color.FromArgb(180, 180, 210), BackColor = Color.Transparent }; p.Controls.Add(c); cy += 24; return c; }
        private void Sep(Panel p, ref int cy) { p.Controls.Add(new Label { Location = new Point(8, cy), Width = 245, Height = 1, BackColor = Color.FromArgb(55, 55, 75) }); cy += 10; }

        private void SetTool(SelectionTool mode, string tip)
        {
            if (_polyActive) { _polyActive = false; _polyImg.Clear(); _polyScr.Clear(); _pictureBox.Invalidate(); }
            _tool = mode; _lblTip.Text = tip;
            for (int i = 0; i < _toolBtns.Length; i++)
            {
                var (m, _) = ((SelectionTool, string))_toolBtns[i].Tag!;
                _toolBtns[i].BackColor = m == mode ? Color.FromArgb(50, 110, 170) : Color.FromArgb(32, 32, 48);
            }
            _pictureBox.Cursor = mode switch { SelectionTool.Polygon => Cursors.UpArrow, SelectionTool.Freehand => Cursors.UpArrow, SelectionTool.AutoDetect => Cursors.Hand, _ => Cursors.Cross };
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys key)
        {
            if (key == Keys.Escape && _polyActive) { _polyActive = false; _polyImg.Clear(); _polyScr.Clear(); _pictureBox.Invalidate(); _slbl.Text = "Polígono cancelado"; return true; }
            if (key == Keys.Return && _polyActive && _polyImg.Count >= 3) { CommitPolygon(); return true; }
            return base.ProcessCmdKey(ref msg, key);
        }

        private void CheckCalibrationReady() => _btnCalibrate.Enabled = _originalCube != null && _whiteCube != null && _darkCube != null;

        private async Task RebuildWorkingCube()
        {
            if (_originalCube == null) return;

            _slbl.Text = "Reconstruyendo pipeline desde el original...";
            _pb.Visible = true; _pb.Style = ProgressBarStyle.Marquee;

            try
            {
                await Task.Run(() =>
                {
                    _baseCube = _originalCube.Clone();

                    if (_stepNormalize && _whiteCube != null && _darkCube != null)
                        _baseCube.Calibrate(_whiteCube, _darkCube);

                    if (_stepAbsorbance && _baseCube.IsCalibrated)
                        _baseCube.ConvertToAbsorbance();

                    if (_stepScatter == ScatterCorrection.SNV)
                        _baseCube.ApplySNV();
                    else if (_stepScatter == ScatterCorrection.MSC)
                        _baseCube.ApplyMSC();

                    if (_stepSG)
                        _baseCube.ApplySavitzkyGolay(_sgWindow, _sgPoly, _sgDeriv);

                    if (_stepMedian)
                        _baseCube.ApplySpatialMedianFilter(3);
                });

                UpdatePipelineLabel();

                if (_chkAnalyze.Checked) await RunAnalysisAsync();
                else { _cube = _baseCube; RefreshDisplay(); }
                ClearSpectrumPlot();
                _slbl.Text = "Pipeline aplicado correctamente.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error en pipeline"); _slbl.Text = "Error al reconstruir pipeline.";
            }
            finally
            {
                _pb.Visible = false; _pb.Style = ProgressBarStyle.Continuous;
            }
        }

        private void UpdatePipelineLabel()
        {
            if (_lblPipeline == null) return;
            var steps = new System.Collections.Generic.List<string> { "Original" };
            if (_stepNormalize) steps.Add("Norm.");
            if (_stepAbsorbance) steps.Add("Abs.");
            if (_stepScatter == ScatterCorrection.SNV) steps.Add("SNV");
            else if (_stepScatter == ScatterCorrection.MSC) steps.Add("MSC");
            if (_stepSG) steps.Add($"SG(W{_sgWindow})");
            if (_stepMedian) steps.Add("Med.");
            _lblPipeline.Text = "Pipeline: " + string.Join(" → ", steps);
        }

        private static void UpdateToggleButton(Button btn, bool active, Color baseColor)
        {
            btn.BackColor = active
                ? Color.FromArgb(Math.Min(255, baseColor.R + 60), Math.Min(255, baseColor.G + 60), Math.Min(255, baseColor.B + 60))
                : baseColor;
            btn.FlatAppearance.BorderColor = active ? Color.FromArgb(100, 220, 120) : Color.FromArgb(70, 70, 100);
        }

        private void Pic_Down(object? s, MouseEventArgs e)
        {
            if (_cube == null || e.Button != MouseButtons.Left) return;
            var pt = MapToImage(e.Location); if (pt == null) return;
            switch (_tool)
            {
                case SelectionTool.Rectangle:
                case SelectionTool.Circle:
                    _isDragging = true; _dragStartScr = e.Location; _dragStartImg = pt.Value; _dragCurScr = e.Location; break;
                case SelectionTool.Freehand:
                    _isDragging = true; _freeImg.Clear(); _freeScr.Clear(); _freeImg.Add(pt.Value); _freeScr.Add(e.Location); break;
                case SelectionTool.Polygon:
                    if (!_polyActive) { _polyActive = true; _polyImg.Clear(); _polyScr.Clear(); }
                    _polyImg.Add(pt.Value); _polyScr.Add(e.Location); _polyMouse = e.Location;
                    _pictureBox.Invalidate(); break;
            }
        }

        private void Pic_Move(object? s, MouseEventArgs e)
        {
            if (_cube == null) return;
            var pt = MapToImage(e.Location);
            _hoverImgPt = pt;

            if (pt != null)
            {
                int x = pt.Value.X, y = pt.Value.Y;
                if (x >= 0 && x < _cube.Samples && y >= 0 && y < _cube.Lines)
                {
                    float v = _cube[_currentBand, y, x];
                    string bandStr = _cmbBands.Items.Count > _currentBand ? _cmbBands.Items[_currentBand].ToString()! : "N/A";
                    _lblCoords.Text = $"  X:{x}  Y:{y}  │  {bandStr}  │  val={v:G5}";
                    if (_graphicalInfoForm != null && !_graphicalInfoForm.IsDisposed)
                    {
                        _graphicalInfoForm.UpdateData(_currentBand, new Point(x, y));
                    }
                }
                else _lblCoords.Text = "";
            }

            switch (_tool)
            {
                case SelectionTool.Rectangle:
                case SelectionTool.Circle:
                    if (_isDragging) { _dragCurScr = e.Location; _pictureBox.Invalidate(); }
                    break;
                case SelectionTool.Polygon:
                    _polyMouse = e.Location; if (_polyActive) _pictureBox.Invalidate(); break;
                case SelectionTool.Freehand:
                    if (_isDragging && pt != null)
                    {
                        var last = _freeScr.Count > 0 ? _freeScr[^1] : e.Location;
                        if (Math.Abs(e.X - last.X) + Math.Abs(e.Y - last.Y) > 2) { _freeImg.Add(pt.Value); _freeScr.Add(e.Location); _pictureBox.Invalidate(); }
                    }
                    break;
            }
            RedrawSpectrumPlot();
        }

        private void Pic_Up(object? s, MouseEventArgs e)
        {
            if (_cube == null) return;
            var pt = MapToImage(e.Location);

            if (e.Button == MouseButtons.Right && pt != null)
            {
                for (int i = _selections.Count - 1; i >= 0; i--)
                {
                    if (_selections[i].Contains(pt.Value)) { using var dlg = new MetadataDialog(_selections[i]); if (dlg.ShowDialog() == DialogResult.OK) RefreshDisplay(); return; }
                }
                return;
            }

            if (e.Button != MouseButtons.Left) return;

            switch (_tool)
            {
                case SelectionTool.Rectangle:
                    {
                        if (!_isDragging) break; _isDragging = false; _pictureBox.Invalidate();
                        if (pt == null) break;
                        int dx = Math.Abs(pt.Value.X - _dragStartImg.X), dy = Math.Abs(pt.Value.Y - _dragStartImg.Y);
                        Color col = NextColor();
                        if (dx < 4 && dy < 4) AddShape(new PixelShape(new Point(_dragStartImg.X, _dragStartImg.Y), col));
                        else
                        {
                            int x1 = Math.Clamp(Math.Min(_dragStartImg.X, pt.Value.X), 0, _cube.Samples - 1), y1 = Math.Clamp(Math.Min(_dragStartImg.Y, pt.Value.Y), 0, _cube.Lines - 1);
                            int x2 = Math.Clamp(Math.Max(_dragStartImg.X, pt.Value.X), 0, _cube.Samples - 1), y2 = Math.Clamp(Math.Max(_dragStartImg.Y, pt.Value.Y), 0, _cube.Lines - 1);
                            AddShape(new RectShape(new Rectangle(x1, y1, x2 - x1, y2 - y1), col));
                        }
                        break;
                    }
                case SelectionTool.Circle:
                    {
                        if (!_isDragging) break; _isDragging = false; _pictureBox.Invalidate();
                        if (pt == null) break;
                        int r = (int)Math.Round(Math.Sqrt(Math.Pow(pt.Value.X - _dragStartImg.X, 2) + Math.Pow(pt.Value.Y - _dragStartImg.Y, 2)));
                        if (r > 1) AddShape(new CircleShape(_dragStartImg, r, NextColor())); break;
                    }
                case SelectionTool.Freehand:
                    {
                        if (!_isDragging) break; _isDragging = false; _pictureBox.Invalidate();
                        if (_freeImg.Count >= 3) AddShape(new FreehandShape(_freeImg, NextColor()));
                        _freeImg.Clear(); _freeScr.Clear(); break;
                    }
                case SelectionTool.AutoDetect:
                    {
                        if (pt != null)
                        {
                            bool addMode = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;
                            bool subMode = (Control.ModifierKeys & Keys.Alt) == Keys.Alt;
                            RunAutoRoi(pt.Value.X, pt.Value.Y, addMode, subMode);
                        }
                        break;
                    }
            }
        }

        private void Pic_DblClick(object? s, MouseEventArgs e) { if (_tool == SelectionTool.Polygon && _polyActive) { if (_polyImg.Count > 0) { _polyImg.RemoveAt(_polyImg.Count - 1); _polyScr.RemoveAt(_polyScr.Count - 1); } CommitPolygon(); } else ClearAll(); }
        private void Pic_Leave(object? s, EventArgs e) { _hoverImgPt = null; _lblCoords.Text = ""; RedrawSpectrumPlot(); }

        private void Pic_Paint(object? s, PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var col = SelColors[_selections.Count % SelColors.Length];

            switch (_tool)
            {
                case SelectionTool.Rectangle:
                    if (!_isDragging) break;
                    int x1 = Math.Min(_dragStartScr.X, _dragCurScr.X), y1 = Math.Min(_dragStartScr.Y, _dragCurScr.Y);
                    int w = Math.Abs(_dragCurScr.X - _dragStartScr.X), h = Math.Abs(_dragCurScr.Y - _dragStartScr.Y);
                    if (w > 1 && h > 1) { g.FillRectangle(new SolidBrush(Color.FromArgb(30, col)), x1, y1, w, h); g.DrawRectangle(new Pen(col, 1.5f) { DashStyle = DashStyle.Dash }, x1, y1, w, h); }
                    break;
                case SelectionTool.Circle:
                    if (!_isDragging) break;
                    float r = (float)Math.Sqrt(Math.Pow(_dragCurScr.X - _dragStartScr.X, 2) + Math.Pow(_dragCurScr.Y - _dragStartScr.Y, 2));
                    if (r > 1) g.DrawEllipse(new Pen(col, 1.5f) { DashStyle = DashStyle.Dash }, _dragStartScr.X - r, _dragStartScr.Y - r, r * 2, r * 2); break;
                case SelectionTool.Polygon:
                    if (!_polyActive || _polyScr.Count == 0) break;
                    var all = _polyScr.Concat(new[] { _polyMouse }).Select(p => (PointF)p).ToArray();
                    if (all.Length >= 3) g.FillPolygon(new SolidBrush(Color.FromArgb(22, col)), all);
                    g.DrawLines(new Pen(col, 1.5f) { DashStyle = DashStyle.Dash }, all); break;
                case SelectionTool.Freehand:
                    if (!_isDragging || _freeScr.Count < 2) break;
                    g.DrawLines(new Pen(col, 1.5f), _freeScr.Select(p => (PointF)p).ToArray()); break;
            }
        }

        private Color NextColor() => SelColors[_selections.Count % SelColors.Length];
        private void AddShape(SelectionShape sh) { if (_selections.Count >= SelColors.Length) _selections.RemoveAt(0); _selections.Add(sh); _btnClear.Enabled = true; RefreshDisplay(); }
        private void CommitPolygon() { if (_polyImg.Count >= 3) AddShape(new PolygonShape(_polyImg, NextColor())); _polyActive = false; _polyImg.Clear(); _polyScr.Clear(); _pictureBox.Invalidate(); }
        private void ClearAll() { _selections.Clear(); _polyActive = false; _polyImg.Clear(); _polyScr.Clear(); _freeImg.Clear(); _freeScr.Clear(); _btnClear.Enabled = false; ClearSpectrumPlot(); _pictureBox.Invalidate(); }

        // --- MOTOR DE SAM PARA AUTO-ROI ADAPTADO PARA SUMAR/RESTAR ---
        private async void RunAutoRoi(int startX, int startY, bool addMode = false, bool subMode = false)
        {
            if (_cube == null) return;

            MaskShape? targetMask = null;
            if ((addMode || subMode) && _selections.Count > 0 && _selections.Last() is MaskShape lastMask)
            {
                targetMask = lastMask;
            }

            _slbl.Text = "🪄 Analizando firma espectral (SAM)..."; _pb.Visible = true; _pb.Style = ProgressBarStyle.Marquee; _pictureBox.Enabled = false;
            float tolPercent = (float)_nudAutoTol.Value / 100f, maxAngleRads = tolPercent * 1.5f, minCos = (float)Math.Cos(maxAngleRads);
            Color col = targetMask?.Color ?? NextColor();
            bool[,] mask = null!;

            await Task.Run(() => {
                int w = _cube.Samples, h = _cube.Lines; mask = new bool[h, w];
                int numBands = 16, step = Math.Max(1, _cube.Bands / numBands);
                var bandsToUse = new List<int>(); for (int b = 0; b < _cube.Bands; b += step) bandsToUse.Add(b);

                float[] refSpec = new float[bandsToUse.Count]; float normRef = 0f;
                for (int i = 0; i < bandsToUse.Count; i++) { float val = _cube[bandsToUse[i], startY, startX]; refSpec[i] = float.IsNaN(val) ? 0 : val; normRef += refSpec[i] * refSpec[i]; }
                normRef = (float)Math.Sqrt(normRef); if (normRef < 1e-6f) return;

                var stack = new Stack<(int x, int y)>(w * h / 4); stack.Push((startX, startY)); mask[startY, startX] = true;

                bool IsSimilar(int cx, int cy)
                {
                    float dot = 0f, normB = 0f;
                    for (int i = 0; i < bandsToUse.Count; i++) { float val = _cube[bandsToUse[i], cy, cx]; if (float.IsNaN(val)) return false; dot += refSpec[i] * val; normB += val * val; }
                    return normB >= 1e-6f && (dot / (normRef * (float)Math.Sqrt(normB))) >= minCos;
                }

                while (stack.Count > 0)
                {
                    var (cx, cy) = stack.Pop();
                    if (cx > 0 && !mask[cy, cx - 1] && IsSimilar(cx - 1, cy)) { mask[cy, cx - 1] = true; stack.Push((cx - 1, cy)); }
                    if (cx < w - 1 && !mask[cy, cx + 1] && IsSimilar(cx + 1, cy)) { mask[cy, cx + 1] = true; stack.Push((cx + 1, cy)); }
                    if (cy > 0 && !mask[cy - 1, cx] && IsSimilar(cx, cy - 1)) { mask[cy - 1, cx] = true; stack.Push((cx, cy - 1)); }
                    if (cy < h - 1 && !mask[cy + 1, cx] && IsSimilar(cx, cy + 1)) { mask[cy + 1, cx] = true; stack.Push((cx, cy + 1)); }
                }
            });

            if (mask != null)
            {
                if (targetMask != null)
                {
                    if (addMode) targetMask.AddMask(mask);
                    else if (subMode) targetMask.RemoveMask(mask);
                    RefreshDisplay();
                }
                else
                {
                    AddShape(new MaskShape(mask, col));
                }
            }
            _pictureBox.Enabled = true; _pb.Visible = false; _pb.Style = ProgressBarStyle.Continuous; _slbl.Text = "✔ Auto ROI completado";
        }

        private async void BtnLoad_Click(object? s, EventArgs e)
        {
            using var dlg = new OpenFileDialog { Title = "Abrir imagen hiperespectral ENVI", Filter = "ENVI Header (*.hdr)|*.hdr|Todos|*.*" };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            _btnLoad.Enabled = false; _pb.Visible = true; _pb.Value = 0; _slbl.Text = "Cargando cubo...";
            var prog = new Progress<int>(v => { _pb.Value = v; _slbl.Text = $"Cargando… {v} %"; });
            try
            {
                _baseCube = await Task.Run(() => HyperspectralCube.Load(dlg.FileName, prog));

                _originalCube = _baseCube.Clone();

                _cube = _baseCube; _selections.Clear(); _chkAnalyze.Checked = false;
                PopulateBandsCombo(); _slider.Minimum = 0; _slider.Maximum = Math.Max(0, _cube.Bands - 1); _slider.Value = 0; _currentBand = 0;
                _loadedFileName = Path.GetFileName(dlg.FileName); this.Text = $"SpecimenFX17 — Visor BLI Hiperespectral - {_loadedFileName}";
                CheckCalibrationReady(); _btnExport.Enabled = _btnExpAll.Enabled = true; RefreshDisplay(); ClearSpectrumPlot();
                _slbl.Text = $"✔ {_cube.Header}";
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Error"); _slbl.Text = "Error"; }
            finally { _pb.Visible = false; _btnLoad.Enabled = true; }
        }

        private void RefreshDisplay()
        {
            if (_cube == null) return;

            int origBands = _baseCube != null ? _baseCube.Header.Bands : _cube.Header.Bands;
            bool isPcaBand = _currentBand >= origBands + 4;

            var opts = new BliRenderOptions
            {
                Colormap = isPcaBand || _grayscaleMode ? BliColormap.Grayscale : (BliColormap)_cmbCmap.SelectedIndex,
                Gamma = isPcaBand ? 1.0f : (float)_nudGamma.Value,
                LowPercentile = isPcaBand ? 0f : (float)_nudLo.Value,
                HighPercentile = isPcaBand ? 100f : (float)_nudHi.Value,
                SignalThreshold = isPcaBand ? 0f : (float)_nudThr.Value,
                DrawColorbar = _chkCbar.Checked && !_rgbMode,
                Wavelength = WlAt(_currentBand),
                WavelengthUnit = _cube.Header.WavelengthUnits
            };

            Bitmap? newBitmap;
            string bandName = _cmbBands.Items.Count > _currentBand ? _cmbBands.Items[_currentBand].ToString()! : $"Banda {_currentBand + 1}";

            if (_rgbMode)
            {
                int bR = GetClosestBand(640), bG = GetClosestBand(550), bB = GetClosestBand(460);
                newBitmap = BliRenderer.RenderRGB(_cube, bR, bG, bB, opts);
                _lblBandInfo.Text = $"Modo RGB\nR: {WlAt(bR):F1} nm\nG: {WlAt(bG):F1} nm\nB: {WlAt(bB):F1} nm\nPx: {_cube.Samples}x{_cube.Lines}";
            }
            else
            {
                newBitmap = BliRenderer.RenderBand(_cube, _currentBand, opts);
                var (mn, mx) = _cube.GetBandStats(_currentBand);
                _lblBandInfo.Text = $"{bandName}\nMín: {mn:G5}\nMáx: {mx:G5}\nPx: {_cube.Samples}x{_cube.Lines}";
            }

            if (_selections.Count > 0)
            {
                using var g = Graphics.FromImage(newBitmap); g.SmoothingMode = SmoothingMode.AntiAlias;
                foreach (var sh in _selections) sh.DrawOn(g);
            }

            Bitmap? oldBitmap = _currentBitmap; _currentBitmap = newBitmap; _pictureBox.Image = _currentBitmap; oldBitmap?.Dispose();
            RedrawSpectrumPlot();
        }

        private int GetClosestBand(double targetWl)
        {
            if (_cube == null || _cube.Header.Wavelengths.Count == 0) return 0;
            return _cube.Header.Wavelengths.Select((wl, i) => (diff: Math.Abs(wl - targetWl), i)).OrderBy(x => x.diff).First().i;
        }

        private void ClearSpectrumPlot() { _specPlot.Image?.Dispose(); _specPlot.Image = null; if (_cube != null) RefreshDisplay(); }

        private void RedrawSpectrumPlot()
        {
            if (_cube == null || (_selections.Count == 0 && _hoverImgPt == null)) return;
            int w = Math.Max(_specPlot.Width, 300), h = Math.Max(_specPlot.Height, 80);
            var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp); g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.FromArgb(12, 12, 20));

            const int pL = 64, pR = 20, pT = 24, pB = 40; var plot = new Rectangle(pL, pT, w - pL - pR, h - pT - pB);
            if (plot.Width < 20 || plot.Height < 10) { _specPlot.Image = bmp; return; }

            int plotBands = _baseCube != null ? _baseCube.Header.Bands : _cube.Header.Bands;

            float yMin = float.MaxValue, yMax = float.MinValue;
            foreach (var sh in _selections)
            {
                foreach (float v in sh.GetSpectrum(_cube).Take(plotBands)) { if (!float.IsNaN(v) && v < yMin) yMin = v; if (!float.IsNaN(v) && v > yMax) yMax = v; }
            }

            float[]? hoverSpec = null;
            if (_hoverImgPt.HasValue)
            {
                int hx = _hoverImgPt.Value.X, hy = _hoverImgPt.Value.Y;
                if (hx >= 0 && hx < _cube.Samples && hy >= 0 && hy < _cube.Lines)
                {
                    hoverSpec = _cube.GetSpectrum(hy, hx).Take(plotBands).ToArray();
                    foreach (float v in hoverSpec) { if (!float.IsNaN(v) && v < yMin) yMin = v; if (!float.IsNaN(v) && v > yMax) yMax = v; }
                }
            }

            if (yMin == float.MaxValue) { yMin = 0; yMax = 1; }
            float yRng = yMax - yMin; if (yRng < 1e-10f) yRng = 1f; yMin -= yRng * 0.05f; yMax += yRng * 0.05f; yRng = yMax - yMin;

            var wls = _cube.Header.Wavelengths;
            double xMin = wls.Count > 0 ? wls[0] : 0, xMax = wls.Count > 0 ? wls[^1] : plotBands - 1, xRng = xMax - xMin;

            using (var gp = new Pen(Color.FromArgb(28, 255, 255, 255), 1f) { DashStyle = DashStyle.Dash })
            {
                for (int i = 0; i <= 5; i++) g.DrawLine(gp, plot.Left, plot.Bottom - (float)i / 5 * plot.Height, plot.Right, plot.Bottom - (float)i / 5 * plot.Height);
                for (int i = 0; i <= 6; i++) g.DrawLine(gp, plot.Left + (float)i / 6 * plot.Width, plot.Top, plot.Left + (float)i / 6 * plot.Width, plot.Bottom);
            }
            g.DrawRectangle(new Pen(Color.FromArgb(65, 255, 255, 255)), plot);

            if (_currentBand < plotBands)
            {
                double curWl = WlAt(_currentBand); float curPx = plot.Left + (float)((curWl - xMin) / xRng * plot.Width);
                g.DrawLine(new Pen(Color.FromArgb(110, 255, 255, 80), 1f) { DashStyle = DashStyle.Dash }, curPx, plot.Top, curPx, plot.Bottom);
            }

            if (hoverSpec != null && hoverSpec.Length > 1)
            {
                var hp = new PointF[hoverSpec.Length];
                for (int i = 0; i < hoverSpec.Length; i++)
                {
                    float px = plot.Left + (float)(((i < wls.Count ? wls[i] : xMin + i * xRng / hoverSpec.Length) - xMin) / xRng * plot.Width);
                    float py = Math.Clamp(plot.Bottom - (hoverSpec[i] - yMin) / yRng * plot.Height, plot.Top - 8, plot.Bottom + 8);
                    hp[i] = new PointF(px, py);
                }
                using var hpen = new Pen(Color.FromArgb(180, 200, 200, 220), 1.5f) { DashStyle = DashStyle.Dot }; g.DrawLines(hpen, hp);
            }

            foreach (var sh in _selections)
            {
                var spec = sh.GetSpectrum(_cube).Take(plotBands).ToArray();
                if (spec.Length > 1)
                {
                    var pts = new PointF[spec.Length];
                    for (int i = 0; i < spec.Length; i++)
                    {
                        float px = plot.Left + (float)(((i < wls.Count ? wls[i] : xMin + i * xRng / spec.Length) - xMin) / xRng * plot.Width);
                        float py = Math.Clamp(plot.Bottom - (spec[i] - yMin) / yRng * plot.Height, plot.Top - 8, plot.Bottom + 8); pts[i] = new PointF(px, py);
                    }
                    g.DrawLines(new Pen(sh.Color, 1.8f), pts);
                }
            }

            using var tf = new Font("Consolas", 7.5f); using var tb = new SolidBrush(Color.FromArgb(160, 160, 195));
            for (int i = 0; i <= 7; i++) { float px = plot.Left + (float)(i / 7.0 * plot.Width); string lb = (xMin + xRng * i / 7).ToString("F0"); var sz = g.MeasureString(lb, tf); g.DrawString(lb, tf, tb, px - sz.Width / 2, plot.Bottom + 2); }
            for (int i = 0; i <= 5; i++) { float py = plot.Bottom - (float)i / 5 * plot.Height; string lb = (yMin + yRng * i / 5).ToString("G4"); var sz = g.MeasureString(lb, tf); g.DrawString(lb, tf, tb, plot.Left - sz.Width - 3, py - sz.Height / 2); }

            using var lf = new Font("Consolas", 7.5f); int lx = plot.Right - 144, ly = plot.Top + 4;
            foreach (var sh in _selections) { g.DrawLine(new Pen(sh.Color, 2f), lx, ly + 5, lx + 16, ly + 5); g.DrawString($"{sh.LegendIcon} {sh.ShortLabel}", lf, new SolidBrush(sh.Color), lx + 20, ly); ly += 14; }
            if (hoverSpec != null) { using var hdash = new Pen(Color.FromArgb(180, 200, 200, 220), 1.5f) { DashStyle = DashStyle.Dot }; g.DrawLine(hdash, lx, ly + 5, lx + 16, ly + 5); g.DrawString($"· (Cursor)", lf, new SolidBrush(Color.FromArgb(180, 200, 200, 220)), lx + 20, ly); }

            var oldImg = _specPlot.Image; _specPlot.Image = bmp; oldImg?.Dispose();
        }

        private void BtnExport_Click(object? s, EventArgs e)
        {
            if (_currentBitmap == null) return;
            using var dlg = new SaveFileDialog { Filter = "PNG (*.png)|*.png", FileName = $"Vista_{(_rgbMode ? "RGB" : $"banda{_currentBand + 1}_{WlAt(_currentBand):F1}nm")}" };
            if (dlg.ShowDialog() == DialogResult.OK) _currentBitmap.Save(dlg.FileName, ImageFormat.Png);
        }

        private async void BtnExportAll_Click(object? s, EventArgs e)
        {
            if (_cube == null) return;
            using var dlg = new FolderBrowserDialog(); if (dlg.ShowDialog() != DialogResult.OK) return;
            _btnExpAll.Enabled = false;
            await Task.Run(() => {
                for (int b = 0; b < _cube.Bands; b++) { var o = new BliRenderOptions { Wavelength = WlAt(b), Colormap = _grayscaleMode ? BliColormap.Grayscale : BliColormap.Rainbow }; using var bmp = BliRenderer.RenderBand(_cube, b, o); bmp.Save(Path.Combine(dlg.SelectedPath, $"b_{b + 1:D3}.png"), ImageFormat.Png); }
            });
            _btnExpAll.Enabled = true;
        }

        private double WlAt(int b) => _cube != null && b < _cube.Header.Wavelengths.Count ? _cube.Header.Wavelengths[b] : b;

        private Point? MapToImage(Point sc)
        {
            if (_currentBitmap == null) return null;
            float scale = Math.Max((float)_currentBitmap.Width / _pictureBox.Width, (float)_currentBitmap.Height / _pictureBox.Height);
            float ox = (_pictureBox.Width - _currentBitmap.Width / scale) / 2f, oy = (_pictureBox.Height - _currentBitmap.Height / scale) / 2f;
            return new Point((int)((sc.X - ox) * scale), (int)((sc.Y - oy) * scale));
        }
    }
}