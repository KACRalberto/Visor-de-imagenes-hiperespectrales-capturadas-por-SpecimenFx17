using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

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

        private readonly List<DockContent> _childForms = new();

        private int _currentBand = 0;
        private bool _grayscaleMode = false;
        private bool _rgbMode = false;
        private Bitmap? _currentBitmap;
        private GraphicalInfoForm? _graphicalInfoForm;

        private readonly List<SelectionShape> _selections = new();
        private Point? _hoverImgPt = null;

        // --- SISTEMA DESHACER / REHACER ---
        private readonly Stack<List<SelectionShape>> _undoStack = new();
        private readonly Stack<List<SelectionShape>> _redoStack = new();
        private Button _btnUndo = null!;
        private Button _btnRedo = null!;

        private SelectionTool _tool = SelectionTool.Rectangle;
        private Button[] _toolBtns = null!;
        private Label _lblTip = null!;

        private bool _isDragging;
        private Point _dragStartScr, _dragStartImg, _dragCurScr;
        private bool _polyActive;
        private List<Point> _polyImg = new(), _polyScr = new();
        private Point _polyMouse;
        private List<Point> _freeImg = new(), _freeScr = new();

        // --- VARIABLES DE ZOOM Y PANEO ---
        private float _zoomFactor = 1.0f;
        private PointF _panOffset = new PointF(0, 0);
        private bool _isPanning = false;
        private Point _lastMousePos;

        private PictureBox _pictureBox = null!;
        private PictureBox _specPlot = null!;
        private Label _lblCoords = null!;
        private Label _lblSpecInfo = null!;
        private Label _lblBandInfo = null!;
        private RichTextBox _txtAnalysisReport = null!;

        private ComboBox _cmbBands = null!;
        private TrackBar _slider = null!;
        private CheckBox _chkAnalyze = null!;

        private ComboBox _cmbCamera = null!;
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
        private Button _btnClose = null!;
        private Label _lblWhite = null!;
        private Label _lblDark = null!;

        private Button _btnCalibrate = null!;
        private Button _btnAbsorbance = null!;
        private Button _btnSnv = null!;
        private Button _btnMsc = null!;
        private Button _btnSg = null!;
        private Button _btnMedian = null!;
        private Button _btnRot = null!;

        private bool _stepNormalize = false;
        private bool _stepAbsorbance = false;
        private enum ScatterCorrection { None, SNV, MSC }
        private ScatterCorrection _stepScatter = ScatterCorrection.None;
        private bool _stepSG = false;
        private int _sgWindow = 15, _sgPoly = 2, _sgDeriv = 1;
        private bool _stepMedian = false;
        private float _stepRotation = 0f;
        private Label _lblPipeline = null!;

        private Button _btnExport = null!;
        private Button _btnExpAll = null!;
        private Button _btnExpMeanSpec = null!;
        private Button _btnExpGraph = null!;
        private Button _btnReport = null!;
        private Button _btnClear = null!;

        private ProgressBar _pb = null!;
        private StatusStrip _ss = null!;
        private ToolStripStatusLabel _slbl = null!;
        private ToolStripButton _btnCancelTask = null!;
        private CancellationTokenSource? _cts;

        private bool _estaCargando = false;

        private DockPanel _dockPanel = null!;
        private DockContent _imageDocument = null!;
        private DockContent _toolWindow = null!;

        private static int Clamp(int val, int min, int max) => Math.Max(min, Math.Min(max, val));
        private static float Clamp(float val, float min, float max) => Math.Max(min, Math.Min(max, val));

        private static System.Drawing.Color GetRandomColor()
        {
            var rnd = new Random();
            return System.Drawing.Color.FromArgb(rnd.Next(100, 256), rnd.Next(100, 256), rnd.Next(100, 256));
        }

        public MainForm()
        {
            Text = "Specimen — Workspace Hiperespectral";
            Size = new Size(1600, 950); MinimumSize = new Size(1000, 700);
            BackColor = Color.FromArgb(45, 45, 48);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9f);
            try { this.Icon = new Icon("favicon.ico"); } catch { }

            IsMdiContainer = true;
            BuildUI();
        }

        private void SaveStateForUndo()
        {
            _undoStack.Push(_selections.ToList());
            _redoStack.Clear();
            UpdateUndoRedoUI();
        }

        private void PerformUndo()
        {
            if (_undoStack.Count == 0) return;
            _redoStack.Push(_selections.ToList());
            _selections.Clear();
            _selections.AddRange(_undoStack.Pop());

            _polyActive = false; _polyImg.Clear(); _polyScr.Clear(); _freeImg.Clear(); _freeScr.Clear();
            _btnClear.Enabled = _selections.Count > 0;
            ClearSpectrumPlot();
            RefreshDisplay();
            UpdateUndoRedoUI();
            _pictureBox.Invalidate();
        }

        private void PerformRedo()
        {
            if (_redoStack.Count == 0) return;
            _undoStack.Push(_selections.ToList());
            _selections.Clear();
            _selections.AddRange(_redoStack.Pop());

            _btnClear.Enabled = _selections.Count > 0;
            ClearSpectrumPlot();
            RefreshDisplay();
            UpdateUndoRedoUI();
            _pictureBox.Invalidate();
        }

        private void UpdateUndoRedoUI()
        {
            if (_btnUndo != null) _btnUndo.Enabled = _undoStack.Count > 0;
            if (_btnRedo != null) _btnRedo.Enabled = _redoStack.Count > 0;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys key)
        {
            if (key == (Keys.Control | Keys.Z)) { PerformUndo(); return true; }
            if (key == (Keys.Control | Keys.Y) || key == (Keys.Control | Keys.Shift | Keys.Z)) { PerformRedo(); return true; }
            if (key == Keys.Escape && _polyActive) { _polyActive = false; _polyImg.Clear(); _polyScr.Clear(); _pictureBox.Invalidate(); _slbl.Text = "Polígono cancelado"; return true; }
            if (key == Keys.Return && _polyActive && _polyImg.Count >= 3) { CommitPolygon(); return true; }
            return base.ProcessCmdKey(ref msg, key);
        }

        private void BuildUI()
        {
            _dockPanel = new DockPanel
            {
                Dock = DockStyle.Fill,
                Theme = new VS2015DarkTheme(),
                DocumentStyle = DocumentStyle.DockingWindow,
                ShowDocumentIcon = true
            };
            Controls.Add(_dockPanel);

            _ss = new StatusStrip { BackColor = Color.FromArgb(0, 122, 204), ForeColor = Color.White, SizingGrip = false };
            _slbl = new ToolStripStatusLabel("Arrastra y suelta un archivo .hdr en la pantalla para empezar") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            _pb = new ProgressBar { Style = ProgressBarStyle.Continuous, Visible = false, Size = new Size(200, 14) };
            _btnCancelTask = new ToolStripButton("🛑 Cancelar proceso") { ForeColor = Color.White, Font = new Font("Segoe UI", 9f, FontStyle.Bold), Visible = false, Margin = new Padding(10, 0, 5, 0) };
            _btnCancelTask.Click += (s, e) => { if (_cts != null && !_cts.IsCancellationRequested) { _cts.Cancel(); _btnCancelTask.Enabled = false; _btnCancelTask.Text = "⏳ Cancelando..."; } };
            _ss.Items.Add(_slbl); _ss.Items.Add(new ToolStripControlHost(_pb)); _ss.Items.Add(_btnCancelTask);
            Controls.Add(_ss);

            var rp = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(30, 30, 30),
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(6, 8, 6, 20)
            };
            BuildRightPanel(rp);

            var cp = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(20, 20, 20) };
            var slPan = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Color.FromArgb(37, 37, 38) };
            var cmbContainer = new Panel { Dock = DockStyle.Left, Width = 260, Padding = new Padding(8, 10, 8, 10) };
            _cmbBands = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(50, 50, 50), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Consolas", 10f) };
            _cmbBands.SelectedIndexChanged += (_, _) => { if (_cmbBands.SelectedIndex >= 0 && _slider.Value != _cmbBands.SelectedIndex) { _slider.Value = _cmbBands.SelectedIndex; _currentBand = _slider.Value; if (_graphicalInfoForm != null && !_graphicalInfoForm.IsDisposed) _graphicalInfoForm.CurrentBand = _currentBand; RefreshDisplay(); } };
            cmbContainer.Controls.Add(_cmbBands);

            _slider = new TrackBar { Dock = DockStyle.Fill, Minimum = 0, Maximum = 0, TickStyle = TickStyle.None, BackColor = Color.FromArgb(37, 37, 38) };
            _slider.Scroll += (_, _) => { _currentBand = _slider.Value; if (_cmbBands.Items.Count > _slider.Value) _cmbBands.SelectedIndex = _slider.Value; if (_graphicalInfoForm != null && !_graphicalInfoForm.IsDisposed) _graphicalInfoForm.CurrentBand = _currentBand; RefreshDisplay(); };

            slPan.Controls.Add(_slider); slPan.Controls.Add(cmbContainer);

            var spCon = new Panel { Dock = DockStyle.Bottom, Height = 200, BackColor = Color.FromArgb(18, 18, 26) };
            _lblSpecInfo = new Label { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 5, 0, 5), BackColor = Color.FromArgb(28, 28, 28), ForeColor = Color.FromArgb(180, 180, 220), Font = new Font("Segoe UI", 8f, FontStyle.Italic), Text = "  Rastreo activo con el ratón  •  Clic Izq = fijar selección  •  Clic Der = metadatos (°Brix)  •  Rueda = Zoom", TextAlign = ContentAlignment.MiddleLeft };
            _specPlot = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(12, 12, 20), SizeMode = PictureBoxSizeMode.Normal };
            _specPlot.Resize += (_, _) => RedrawSpectrumPlot();
            spCon.Controls.Add(_specPlot); spCon.Controls.Add(_lblSpecInfo);

            var div = new Panel { Dock = DockStyle.Bottom, Height = 3, BackColor = Color.FromArgb(0, 122, 204) };

            _pictureBox = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Normal, BackColor = Color.Black, Cursor = Cursors.Cross };

            _pictureBox.MouseEnter += (s, e) => _pictureBox.Focus();

            _pictureBox.MouseWheel += (s, e) => {
                if (e.Delta > 0) _zoomFactor *= 1.1f;
                else _zoomFactor /= 1.1f;
                _zoomFactor = Math.Clamp(_zoomFactor, 1.0f, 20.0f);
                _pictureBox.Invalidate();
            };

            _pictureBox.MouseDown += Pic_Down;
            _pictureBox.MouseUp += Pic_Up;
            _pictureBox.MouseMove += Pic_Move;
            _pictureBox.Paint += Pic_Paint;
            _pictureBox.MouseDoubleClick += Pic_DblClick;
            _pictureBox.MouseLeave += Pic_Leave;

            _pictureBox.AllowDrop = true;
            _pictureBox.DragEnter += HandleDragEnter;
            _pictureBox.DragDrop += HandleDragDrop;

            _lblCoords = new Label { AutoSize = true, Location = new Point(6, 6), BackColor = Color.FromArgb(160, 0, 0, 0), ForeColor = Color.FromArgb(200, 255, 200), Font = new Font("Consolas", 8f), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6) };
            _pictureBox.Controls.Add(_lblCoords);

            cp.Controls.Add(_pictureBox); cp.Controls.Add(div); cp.Controls.Add(spCon); cp.Controls.Add(slPan);

            _imageDocument = new DockContent { Text = "📸 Vista Principal", CloseButtonVisible = false, BackColor = Color.Black, HideOnClose = true };
            _imageDocument.Controls.Add(cp);
            _imageDocument.FormClosing += (s, e) => { if (e.CloseReason == CloseReason.UserClosing) e.Cancel = true; };

            _toolWindow = new DockContent { Text = "⚙️ Herramientas", CloseButtonVisible = false, BackColor = Color.FromArgb(30, 30, 30), HideOnClose = true };
            _toolWindow.Controls.Add(rp);
            _toolWindow.FormClosing += (s, e) => { if (e.CloseReason == CloseReason.UserClosing) e.Cancel = true; };

            rp.Layout += (s, e) => { if (rp.ClientSize.Width == 0) return; int targetWidth = rp.ClientSize.Width - rp.Padding.Left - rp.Padding.Right; foreach (Control c in rp.Controls) c.Width = targetWidth - c.Margin.Left - c.Margin.Right; };

            _toolWindow.Show(_dockPanel, DockState.DockRight);
            _imageDocument.Show(_dockPanel, DockState.Document);

            _dockPanel.DockRightPortion = 360d / this.Width;
        }

        private Button Btn(FlowLayoutPanel p, string t, Color bg)
        {
            var b = new Button { Text = t, AutoSize = true, Padding = new Padding(10, 8, 10, 8), FlatStyle = FlatStyle.Flat, BackColor = bg, ForeColor = Color.White, Cursor = Cursors.Hand, Margin = new Padding(8, 4, 8, 4) };
            b.FlatAppearance.BorderColor = Color.FromArgb(Math.Min(255, bg.R + 35), Math.Min(255, bg.G + 35), Math.Min(255, bg.B + 35)); p.Controls.Add(b); return b;
        }
        private NumericUpDown Num(FlowLayoutPanel p, string lblText, decimal v, decimal mn, decimal mx, decimal inc, int dec)
        {
            Lbl(p, lblText); var n = new NumericUpDown { Minimum = mn, Maximum = mx, Value = v, Increment = inc, DecimalPlaces = dec, BackColor = Color.FromArgb(45, 45, 48), ForeColor = Color.White, Margin = new Padding(8, 0, 8, 8) };
            p.Controls.Add(n); return n;
        }
        private void Lbl(FlowLayoutPanel p, string t) { p.Controls.Add(new Label { Text = t, AutoSize = true, ForeColor = Color.FromArgb(140, 140, 170), Font = new Font("Segoe UI", 8.5f), Margin = new Padding(8, 4, 8, 0) }); }
        private void Sec(FlowLayoutPanel p, string t) { p.Controls.Add(new Label { Text = t, AutoSize = true, ForeColor = Color.FromArgb(100, 160, 220), Font = new Font("Segoe UI", 8f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(8, 12, 8, 4) }); }
        private CheckBox Chk(FlowLayoutPanel p, string t, bool v) { var c = new CheckBox { Text = t, AutoSize = true, Checked = v, ForeColor = Color.FromArgb(180, 180, 210), BackColor = Color.Transparent, Margin = new Padding(8, 2, 8, 2) }; p.Controls.Add(c); return c; }
        private void Sep(FlowLayoutPanel p) { p.Controls.Add(new Label { AutoSize = false, Height = 2, BackColor = Color.FromArgb(55, 55, 75), Margin = new Padding(8, 8, 8, 8) }); }

        private void BuildRightPanel(FlowLayoutPanel p)
        {
            Sec(p, "CONFIGURACIÓN DEL SENSOR");
            Lbl(p, "Modelo de Cámara:");
            _cmbCamera = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(45, 45, 48), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Margin = new Padding(8, 0, 8, 12) };
            _cmbCamera.Items.AddRange(new string[] { "Specim FX17", "Specim FX10", "IDS Headwall" });
            _cmbCamera.SelectedIndex = 0;

            _cmbCamera.SelectedIndexChanged += (_, _) => {
                string currentCam = _cmbCamera.SelectedItem.ToString();
                this.Text = string.IsNullOrEmpty(_loadedFileName) ? $"Specimen — Workspace ({currentCam})" : $"Specimen — [{_loadedFileName}] ({currentCam})";
                _slbl.Text = $"Cámara configurada: {currentCam}";
            };
            p.Controls.Add(_cmbCamera);
            Sep(p);

            _btnLoad = Btn(p, "📂  Cargar Cubo .hdr/.bil", Color.FromArgb(0, 122, 204));
            _btnLoad.Click += (s, e) => {
                using var dlg = new OpenFileDialog { Title = "Abrir imagen hiperespectral ENVI", Filter = "Imágenes ENVI (*.hdr, *.bil, *.raw)|*.hdr;*.bil;*.raw|Todos los archivos|*.*" };
                if (dlg.ShowDialog() == DialogResult.OK) LoadCubeFromFile(dlg.FileName);
            };

            _btnClose = Btn(p, "❌  Cerrar Imagen", Color.FromArgb(140, 50, 50));
            _btnClose.Click += BtnClose_Click;
            _btnClose.Enabled = false;

            var btnReset = Btn(p, "🔄  Restaurar Original", Color.FromArgb(120, 50, 50));
            btnReset.Click += (s, e) => {
                if (_originalCube == null) return;
                foreach (var f in _childForms.ToList()) f.Close();
                _baseCube = _originalCube.Clone(); _cube = _baseCube;
                ResetPipelineUI(); _chkAnalyze.Checked = false; _txtAnalysisReport.Text = "";
                PopulateBandsCombo(); _slider.Maximum = _cube.Bands - 1; _currentBand = 0; _slider.Value = 0; _cmbBands.SelectedIndex = 0;
                ClearAll(); RefreshDisplay(); ClearSpectrumPlot(); UpdatePipelineLabel();
                _zoomFactor = 1.0f; _panOffset = new PointF(0, 0);
                _slbl.Text = "Imagen restaurada al estado original crudo.";
            };

            Sep(p); Sec(p, "CALIBRACIÓN (Ref. B/N)");

            var btnLoadWhite = Btn(p, "⚪ Cargar Ref. Blanca", Color.FromArgb(80, 80, 80));
            _lblWhite = new Label { AutoSize = true, ForeColor = Color.Gray, Font = new Font("Segoe UI", 8f), Text = "Sin cargar", Margin = new Padding(8, 0, 8, 4) };
            p.Controls.Add(_lblWhite);

            var btnLoadDark = Btn(p, "⚫ Cargar Ref. Oscura", Color.FromArgb(40, 40, 40));
            _lblDark = new Label { AutoSize = true, ForeColor = Color.Gray, Font = new Font("Segoe UI", 8f), Text = "Sin cargar", Margin = new Padding(8, 0, 8, 4) };
            p.Controls.Add(_lblDark);

            var btnClearRefs = Btn(p, "❌ Quitar Referencias B/N", Color.FromArgb(100, 45, 45));
            btnClearRefs.Click += async (s, e) => {
                _whiteCube = null; _darkCube = null; _lblWhite.Text = "Sin cargar"; _lblDark.Text = "Sin cargar"; CheckCalibrationReady();
                _slbl.Text = "Referencias blanca y oscura eliminadas de la memoria.";
                if (_stepNormalize) { _stepNormalize = false; _stepAbsorbance = false; UpdateToggleButton(_btnCalibrate, false, Color.FromArgb(120, 80, 40)); UpdateToggleButton(_btnAbsorbance, false, Color.FromArgb(100, 40, 80)); await RebuildWorkingCube(); }
            };

            _btnCalibrate = Btn(p, "✨ Normalizar Imagen", Color.FromArgb(120, 80, 40)); _btnCalibrate.Enabled = false;
            _btnAbsorbance = Btn(p, "🧪 Convertir a Absorbancia", Color.FromArgb(100, 40, 80)); _btnAbsorbance.Enabled = false;

            btnLoadWhite.Click += async (s, e) => { using var dlg = new OpenFileDialog { Filter = "Imágenes ENVI (*.hdr, *.bil, *.raw)|*.hdr;*.bil;*.raw|Todos los archivos|*.*" }; if (dlg.ShowDialog() == DialogResult.OK) { _slbl.Text = "Cargando referencia blanca..."; try { _whiteCube = await Task.Run(() => HyperspectralCube.Load(dlg.FileName)); _lblWhite.Text = Path.GetFileName(dlg.FileName); CheckCalibrationReady(); _slbl.Text = "Referencia blanca cargada."; } catch (Exception ex) { MessageBox.Show(ex.Message, "Error"); _slbl.Text = "Error."; } } };
            btnLoadDark.Click += async (s, e) => { using var dlg = new OpenFileDialog { Filter = "Imágenes ENVI (*.hdr, *.bil, *.raw)|*.hdr;*.bil;*.raw|Todos los archivos|*.*" }; if (dlg.ShowDialog() == DialogResult.OK) { _slbl.Text = "Cargando referencia oscura..."; try { _darkCube = await Task.Run(() => HyperspectralCube.Load(dlg.FileName)); _lblDark.Text = Path.GetFileName(dlg.FileName); CheckCalibrationReady(); _slbl.Text = "Referencia oscura cargada."; } catch (Exception ex) { MessageBox.Show(ex.Message, "Error"); _slbl.Text = "Error."; } } };

            _btnCalibrate.Click += async (s, e) => { if (_originalCube == null || _whiteCube == null || _darkCube == null) return; _stepNormalize = !_stepNormalize; if (!_stepNormalize) { _stepAbsorbance = false; } _btnAbsorbance.Enabled = _stepNormalize; UpdateToggleButton(_btnCalibrate, _stepNormalize, Color.FromArgb(120, 80, 40)); UpdateToggleButton(_btnAbsorbance, _stepAbsorbance, Color.FromArgb(100, 40, 80)); await RebuildWorkingCube(); };
            _btnAbsorbance.Click += async (s, e) => { if (!_stepNormalize) return; _stepAbsorbance = !_stepAbsorbance; UpdateToggleButton(_btnAbsorbance, _stepAbsorbance, Color.FromArgb(100, 40, 80)); await RebuildWorkingCube(); };

            Sep(p); Sec(p, "PREPROCESAMIENTO QUIMIOMÉTRICO");

            _lblPipeline = new Label { AutoSize = true, ForeColor = Color.FromArgb(100, 200, 130), BackColor = Color.FromArgb(18, 32, 22), Font = new Font("Consolas", 8f), Text = "Pipeline: Original", TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6), Margin = new Padding(8, 4, 8, 8) };
            p.Controls.Add(_lblPipeline);

            Lbl(p, "⚠ SNV y MSC son mutuamente excluyentes:");
            _btnSnv = Btn(p, "📈  SNV (activo: NO)", Color.FromArgb(40, 80, 90));
            _btnMsc = Btn(p, "📉  MSC (activo: NO)", Color.FromArgb(50, 60, 90));

            Lbl(p, "Savitzky-Golay (Vent | Ord | Deriv):");
            var pnlSg = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(8, 0, 8, 4) };
            var nudSgWin = new NumericUpDown { Width = 60, Minimum = 3, Maximum = 51, Value = 15, Increment = 2, BackColor = Color.FromArgb(45, 45, 48), ForeColor = Color.White };
            var nudSgPol = new NumericUpDown { Width = 60, Minimum = 1, Maximum = 5, Value = 2, BackColor = Color.FromArgb(45, 45, 48), ForeColor = Color.White };
            var nudSgDer = new NumericUpDown { Width = 60, Minimum = 0, Maximum = 2, Value = 1, BackColor = Color.FromArgb(45, 45, 48), ForeColor = Color.White };
            pnlSg.Controls.Add(nudSgWin); pnlSg.Controls.Add(nudSgPol); pnlSg.Controls.Add(nudSgDer); p.Controls.Add(pnlSg);
            _btnSg = Btn(p, "〰️  Savitzky-Golay (activo: NO)", Color.FromArgb(90, 70, 50));

            _btnSnv.Click += async (s, e) => { if (_originalCube == null) return; _stepScatter = (_stepScatter == ScatterCorrection.SNV) ? ScatterCorrection.None : ScatterCorrection.SNV; UpdateToggleButton(_btnSnv, _stepScatter == ScatterCorrection.SNV, Color.FromArgb(40, 80, 90)); UpdateToggleButton(_btnMsc, _stepScatter == ScatterCorrection.MSC, Color.FromArgb(50, 60, 90)); _btnSnv.Text = $"📈  SNV (activo: {(_stepScatter == ScatterCorrection.SNV ? "SÍ" : "NO")})"; _btnMsc.Text = $"📉  MSC (activo: {(_stepScatter == ScatterCorrection.MSC ? "SÍ" : "NO")})"; await RebuildWorkingCube(); };
            _btnMsc.Click += async (s, e) => { if (_originalCube == null) return; _stepScatter = (_stepScatter == ScatterCorrection.MSC) ? ScatterCorrection.None : ScatterCorrection.MSC; UpdateToggleButton(_btnSnv, _stepScatter == ScatterCorrection.SNV, Color.FromArgb(40, 80, 90)); UpdateToggleButton(_btnMsc, _stepScatter == ScatterCorrection.MSC, Color.FromArgb(50, 60, 90)); _btnSnv.Text = $"📈  SNV (activo: {(_stepScatter == ScatterCorrection.SNV ? "SÍ" : "NO")})"; _btnMsc.Text = $"📉  MSC (activo: {(_stepScatter == ScatterCorrection.MSC ? "SÍ" : "NO")})"; await RebuildWorkingCube(); };
            _btnSg.Click += async (s, e) => { if (_originalCube == null) return; _stepSG = !_stepSG; if (_stepSG) { _sgWindow = (int)nudSgWin.Value; _sgPoly = (int)nudSgPol.Value; _sgDeriv = (int)nudSgDer.Value; } UpdateToggleButton(_btnSg, _stepSG, Color.FromArgb(90, 70, 50)); _btnSg.Text = $"〰️  Savitzky-Golay (activo: {(_stepSG ? "SÍ" : "NO")})"; await RebuildWorkingCube(); };
            nudSgWin.ValueChanged += async (_, _) => { if (_stepSG) { _sgWindow = (int)nudSgWin.Value; await RebuildWorkingCube(); } }; nudSgPol.ValueChanged += async (_, _) => { if (_stepSG) { _sgPoly = (int)nudSgPol.Value; await RebuildWorkingCube(); } }; nudSgDer.ValueChanged += async (_, _) => { if (_stepSG) { _sgDeriv = (int)nudSgDer.Value; await RebuildWorkingCube(); } };

            Sep(p); Sec(p, "ULTRAVISOR — AUTOMATIZACIÓN");
            var btnAutoSegment = Btn(p, "🔪 Segmentación Automática", Color.FromArgb(0, 80, 80));
            btnAutoSegment.Click += BtnAutoSegment_Click;

            var btnAnalyzeFolder = Btn(p, "🖼️ Analizar carpeta (Galería)", Color.FromArgb(40, 80, 110));
            btnAnalyzeFolder.Click += BtnAnalyzeFolder_Click;

            Sep(p); Sec(p, "HERRAMIENTA DE SELECCIÓN");
            _lblTip = new Label { AutoSize = true, ForeColor = Color.FromArgb(120, 200, 120), Font = new Font("Segoe UI", 8f, FontStyle.Italic), Text = "Arrastra para seleccionar un rectángulo", Margin = new Padding(8, 0, 8, 8) };
            p.Controls.Add(_lblTip);

            var pnlUndo = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(8, 0, 8, 8) };
            _btnUndo = new Button { Text = "↩ Deshacer", AutoSize = true, Padding = new Padding(8, 4, 8, 4), BackColor = Color.FromArgb(50, 50, 60), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Enabled = false, Cursor = Cursors.Hand };
            _btnRedo = new Button { Text = "↪ Rehacer", AutoSize = true, Padding = new Padding(8, 4, 8, 4), BackColor = Color.FromArgb(50, 50, 60), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Enabled = false, Cursor = Cursors.Hand };
            _btnUndo.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 90);
            _btnRedo.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 90);
            _btnUndo.Click += (_, _) => PerformUndo();
            _btnRedo.Click += (_, _) => PerformRedo();
            pnlUndo.Controls.Add(_btnUndo); pnlUndo.Controls.Add(_btnRedo);
            p.Controls.Add(pnlUndo);

            var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, RowCount = 3, BackColor = Color.Transparent, Margin = new Padding(8, 0, 8, 8) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize)); grid.RowStyles.Add(new RowStyle(SizeType.AutoSize)); grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var defs = new (string Lbl, SelectionTool Mode, string Tip)[] { ("▭ Rectángulo", SelectionTool.Rectangle, "Arrastra para rectángulo"), ("⬟ Polígono", SelectionTool.Polygon, "Clic = vértice • Enter = cerrar"), ("○ Círculo", SelectionTool.Circle, "Arrastra desde el centro"), ("✏ Lasso", SelectionTool.Freehand, "Mantén pulsado y dibuja"), ("📍 Punto", SelectionTool.Point, "Clic para punto aislado") };
            _toolBtns = new Button[5];
            for (int i = 0; i < 5; i++)
            {
                var (lbl, mode, tip) = defs[i];
                var tb = new Button
                {
                    Text = lbl,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(2),
                    AutoSize = true,
                    Padding = new Padding(4),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = mode == _tool ? Color.FromArgb(0, 122, 204) : Color.FromArgb(45, 45, 48),
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 8f),
                    Cursor = Cursors.Hand,
                    Tag = (mode, tip)
                };
                tb.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 100);
                tb.Click += (_, _) => { SetTool(mode, tip); };
                grid.Controls.Add(tb, i % 2, i / 2); _toolBtns[i] = tb;
            }
            p.Controls.Add(grid);

            Sep(p); Sec(p, "DATOS Y SESIONES");
            var btnSaveSes = Btn(p, "💾  Guardar Sesión", Color.FromArgb(50, 80, 60)); var btnLoadSes = Btn(p, "📂  Cargar Sesión", Color.FromArgb(60, 60, 80)); var btnDual = Btn(p, "⚖️  Comparador Multifichero", Color.FromArgb(90, 60, 90));
            var btnBatch = Btn(p, "⚙️  Procesamiento por Lotes", Color.FromArgb(140, 100, 40)); btnBatch.Click += BtnBatch_Click;

            btnSaveSes.Click += (_, _) => { if (_cube == null || _selections.Count == 0) { MessageBox.Show("No hay datos para guardar."); return; } using var sfd = new SaveFileDialog { Filter = "Sesión JSON (*.json)|*.json" }; if (sfd.ShowDialog() == DialogResult.OK) SessionManager.SaveSession(sfd.FileName, _selections); };
            btnLoadSes.Click += (_, _) => {
                if (_cube == null) { MessageBox.Show("Carga un cubo primero."); return; }
                using var ofd = new OpenFileDialog { Filter = "Sesión JSON (*.json)|*.json" };
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    SaveStateForUndo();
                    var loaded = SessionManager.LoadSession(ofd.FileName, _cube.Samples, _cube.Lines);
                    _selections.Clear(); _selections.AddRange(loaded); _btnClear.Enabled = true; RefreshDisplay(); _pictureBox.Invalidate();
                    if (_stepRotation != 0f) MessageBox.Show("Nota: La imagen actual está rotada. Si el JSON original no lo estaba, las ROIs estarán desalineadas.", "Advertencia Geométrica", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            btnDual.Click += (_, _) => OpenChildForm(new DualViewerForm(_cube));

            Sep(p); Sec(p, "ANÁLISIS ESPECTRAL");
            _chkAnalyze = Chk(p, "Analizar bandas (Media, Min/Max, PCA)", false); _chkAnalyze.CheckedChanged += ChkAnalyze_CheckedChanged;
            var btnGraph = Btn(p, "📊  Ver información gráfica", Color.FromArgb(60, 100, 140)); btnGraph.Click += (_, _) => { if (_cube == null) return; if (_graphicalInfoForm == null || _graphicalInfoForm.IsDisposed) { _graphicalInfoForm = new GraphicalInfoForm(_cube); OpenChildForm(_graphicalInfoForm); } else _graphicalInfoForm.BringToFront(); };
            var btnCompare = Btn(p, "⚖️  Comparativa ROI (Orig vs Tratada)", Color.FromArgb(100, 80, 140)); btnCompare.Click += (s, e) => { if (_originalCube == null || _cube == null) return; if (_selections.Count == 0) { MessageBox.Show("Selecciona al menos un ROI para comparar las curvas.", "Aviso"); return; } OpenChildForm(new RoiComparisonForm(_originalCube, _cube, _selections.ToList(), _currentBand)); };
            var bc = Btn(p, "🧮  Calculadora de Fórmulas", Color.FromArgb(70, 45, 110)); var ba = Btn(p, "🔬  Herramientas Avanzadas", Color.FromArgb(140, 70, 45)); var bp = Btn(p, "🍊  Predecir °Brix (PLS)", Color.FromArgb(140, 90, 30));
            var b3d = Btn(p, "🧊  Visor de Hipercubo 3D", Color.FromArgb(40, 110, 130)); b3d.Click += (_, _) => { if (_cube != null) OpenChildForm(new Hypercube3DForm(_cube, _selections.AsReadOnly())); };
            bc.Click += (_, _) => { if (_cube != null) OpenChildForm(new SpectralCalculatorForm(_cube, _selections.AsReadOnly())); }; ba.Click += (_, _) => { if (_cube != null) OpenChildForm(new AdvancedAnalysisForm(_cube, _selections.AsReadOnly())); }; bp.Click += (_, _) => { if (_cube != null) OpenChildForm(new PlsPredictionForm(_cube, _selections.AsReadOnly())); };

            _txtAnalysisReport = new RichTextBox { Height = 100, ForeColor = Color.LightGray, BackColor = Color.FromArgb(20, 20, 28), Font = new Font("Consolas", 8f), ReadOnly = true, Margin = new Padding(8, 4, 8, 8) }; p.Controls.Add(_txtAnalysisReport);

            Sep(p); Sec(p, "VISUALIZACIÓN Y VISTA");

            var btnResetView = Btn(p, "🏠 Vista Original (Centrar y ajustar)", Color.FromArgb(60, 60, 60));
            btnResetView.Click += (s, e) => {
                _zoomFactor = 1.0f;
                _panOffset = new PointF(0, 0);
                _pictureBox.Invalidate();
                _slbl.Text = "Vista reseteada.";
            };

            Lbl(p, "Paleta de color:"); _cmbCmap = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(45, 45, 48), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Margin = new Padding(8, 0, 8, 8) }; _cmbCmap.Items.AddRange(Enum.GetNames(typeof(BliColormap))); _cmbCmap.SelectedIndex = 0; _cmbCmap.SelectedIndexChanged += (_, _) => RefreshDisplay(); p.Controls.Add(_cmbCmap);
            _chkCbar = Chk(p, "Mostrar barra de escala", true); _chkGray = Chk(p, "Modo escala de grises", false); _chkRgb = Chk(p, "Modo RGB (Color real visible)", false);
            _chkCbar.CheckedChanged += (_, _) => RefreshDisplay();
            _chkGray.CheckedChanged += (_, _) => { _grayscaleMode = _chkGray.Checked; if (_grayscaleMode) _chkRgb.Checked = false; _cmbCmap.Enabled = !_grayscaleMode && !_rgbMode; RefreshDisplay(); };
            _chkRgb.CheckedChanged += (_, _) => { _rgbMode = _chkRgb.Checked; if (_rgbMode) _chkGray.Checked = false; _cmbCmap.Enabled = !_grayscaleMode && !_rgbMode; _slider.Enabled = !_rgbMode; _cmbBands.Enabled = !_rgbMode; RefreshDisplay(); };

            Sep(p); Sec(p, "AJUSTES DE IMAGEN");
            _nudGamma = Num(p, "Gamma (1 = lineal):", 1.0m, 0.1m, 5.0m, 0.1m, 1);
            _nudLo = Num(p, "Percentil bajo (%):", 2m, 0m, 49m, 1m, 0);
            _nudHi = Num(p, "Percentil alto (%):", 98m, 51m, 100m, 1m, 0);
            _nudThr = Num(p, "Umbral de señal:", 0m, 0m, 9999999m, 1m, 0);
            foreach (var n in new[] { _nudGamma, _nudLo, _nudHi, _nudThr }) n.ValueChanged += (_, _) => RefreshDisplay();

            _btnMedian = Btn(p, "🌫️ Filtro Mediana 3x3 (activo: NO)", Color.FromArgb(70, 90, 110));
            _btnMedian.Click += async (s, e) => { if (_originalCube == null) return; _stepMedian = !_stepMedian; UpdateToggleButton(_btnMedian, _stepMedian, Color.FromArgb(70, 90, 110)); _btnMedian.Text = $"🌫️ Filtro Mediana 3x3 (activo: {(_stepMedian ? "SÍ" : "NO")})"; await RebuildWorkingCube(); };

            Sep(p); Sec(p, "ROTACIÓN ESPACIAL 2D");
            var nudRot = Num(p, "Ángulo de giro (grados):", 0m, -360m, 360m, 1m, 1);
            _btnRot = Btn(p, "🔄 Aplicar Rotación", Color.FromArgb(90, 60, 110));
            _btnRot.Click += async (s, e) => { if (_originalCube == null) return; _stepRotation = (float)nudRot.Value; UpdateToggleButton(_btnRot, _stepRotation != 0f, Color.FromArgb(90, 60, 110)); ClearAll(); await RebuildWorkingCube(); };

            Sep(p); Sec(p, "EXPORTAR IMÁGENES Y REPORTES");
            _btnExport = Btn(p, "💾  Exportar vista actual", Color.FromArgb(35, 95, 55));
            _btnExpAll = Btn(p, "📦  Exportar todas las bandas", Color.FromArgb(30, 75, 45));
            _btnExpMeanSpec = Btn(p, "📉  Exportar espectro medio (CSV)", Color.FromArgb(35, 75, 95));
            _btnExpGraph = Btn(p, "📈  Exportar gráfica inferior", Color.FromArgb(75, 35, 95));
            _btnReport = Btn(p, "📄  Generar Informe PDF", Color.FromArgb(140, 50, 50));
            _btnClear = Btn(p, "🗑️  Limpiar selecciones", Color.FromArgb(110, 40, 40));

            _btnExport.Enabled = _btnExpAll.Enabled = _btnExpMeanSpec.Enabled = _btnExpGraph.Enabled = _btnReport.Enabled = _btnClear.Enabled = false;

            _btnExport.Click += BtnExport_Click;
            _btnExpAll.Click += BtnExportAll_Click;
            _btnExpMeanSpec.Click += BtnExpMeanSpec_Click;
            _btnExpGraph.Click += BtnExpGraph_Click;
            _btnReport.Click += BtnReport_Click;
            _btnClear.Click += (_, _) => ClearAll();

            Sep(p); Sec(p, "INFO DE BANDA");
            _lblBandInfo = new Label { AutoSize = true, ForeColor = Color.FromArgb(160, 160, 190), Font = new Font("Consolas", 8f), Text = "—", Margin = new Padding(8, 0, 8, 8) }; p.Controls.Add(_lblBandInfo);
        }

        private void BtnClose_Click(object? s, EventArgs e)
        {
            foreach (var f in _childForms.ToList()) f.Close();
            _childForms.Clear();

            _originalCube = null; _baseCube = null; _cube = null; _loadedFileName = ""; this.Text = "Specimen — Workspace Hiperespectral";
            _selections.Clear(); _hoverImgPt = null;
            _currentBitmap?.Dispose(); _currentBitmap = null; _pictureBox.Image = null;
            _specPlot.Image?.Dispose(); _specPlot.Image = null;

            _undoStack.Clear(); _redoStack.Clear(); UpdateUndoRedoUI();

            _cmbBands.Items.Clear(); _slider.Minimum = 0; _slider.Maximum = 0; _slider.Value = 0; _currentBand = 0;

            _txtAnalysisReport.Text = ""; _lblBandInfo.Text = "—"; _lblCoords.Text = "";
            ResetPipelineUI(); _chkAnalyze.Checked = false;

            _zoomFactor = 1.0f; _panOffset = new PointF(0, 0);

            _btnExport.Enabled = false; _btnExpAll.Enabled = false; _btnExpMeanSpec.Enabled = false; _btnExpGraph.Enabled = false;
            _btnReport.Enabled = false; _btnClear.Enabled = false; _btnClose.Enabled = false; _btnCalibrate.Enabled = false; _btnAbsorbance.Enabled = false;

            _slbl.Text = "Imagen cerrada. Memoria liberada."; GC.Collect(); GC.WaitForPendingFinalizers();
        }

        private void ResetPipelineUI()
        {
            _stepNormalize = false; _stepAbsorbance = false; _stepScatter = ScatterCorrection.None; _stepSG = false; _stepMedian = false; _stepRotation = 0f;
            UpdateToggleButton(_btnCalibrate, false, Color.FromArgb(120, 80, 40)); UpdateToggleButton(_btnAbsorbance, false, Color.FromArgb(100, 40, 80)); UpdateToggleButton(_btnSnv, false, Color.FromArgb(40, 80, 90)); UpdateToggleButton(_btnMsc, false, Color.FromArgb(50, 60, 90)); UpdateToggleButton(_btnSg, false, Color.FromArgb(90, 70, 50)); UpdateToggleButton(_btnMedian, false, Color.FromArgb(70, 90, 110)); UpdateToggleButton(_btnRot, false, Color.FromArgb(90, 60, 110));
            _btnSnv.Text = "📈  SNV (activo: NO)"; _btnMsc.Text = "📉  MSC (activo: NO)"; _btnSg.Text = "〰️  Savitzky-Golay (activo: NO)"; _btnMedian.Text = "🌫️ Filtro Mediana 3x3 (activo: NO)";
            UpdatePipelineLabel();
        }

        private void BtnReport_Click(object? s, EventArgs e)
        {
            var currentCube = _cube;
            if (currentCube == null) return;
            using var sfd = new SaveFileDialog { Filter = "Documento PDF (*.pdf)|*.pdf", FileName = $"Informe_Specimen_{Path.GetFileNameWithoutExtension(_loadedFileName)}.pdf", Title = "Guardar Reporte Científico" };
            if (sfd.ShowDialog() != DialogResult.OK) return;

            _slbl.Text = "Generando PDF..."; _pb.Visible = true;
            try
            {
                using var pd = new PrintDocument(); pd.PrinterSettings.PrinterName = "Microsoft Print to PDF"; pd.PrinterSettings.PrintToFile = true; pd.PrinterSettings.PrintFileName = sfd.FileName;
                int currentStep = 0;
                pd.PrintPage += (sender, args) => {
                    var g = args.Graphics!; g.SmoothingMode = SmoothingMode.AntiAlias; int y = 50, margin = 50, width = args.PageBounds.Width - margin * 2;
                    using var fTitle = new Font("Segoe UI", 16, FontStyle.Bold); using var fSub = new Font("Segoe UI", 12, FontStyle.Bold); using var fNorm = new Font("Segoe UI", 10); using var fMono = new Font("Consolas", 9);
                    if (currentStep == 0)
                    {
                        g.DrawString("REPORTE CIENTÍFICO HIPERESPECTRAL", fTitle, Brushes.Black, margin, y); y += 30; g.DrawString($"Archivo: {_loadedFileName}   |   Fecha: {DateTime.Now:g}", fNorm, Brushes.DimGray, margin, y); y += 25; g.DrawLine(Pens.LightGray, margin, y, margin + width, y); y += 20;
                        if (_currentBitmap != null) { g.DrawString("1. Vista de la Muestra y ROIs", fSub, Brushes.Black, margin, y); y += 25; float imgRatio = (float)_currentBitmap.Height / _currentBitmap.Width; int dW = Math.Min(width, 400), dH = (int)(dW * imgRatio); if (dH > 280) { dH = 280; dW = (int)(dH / imgRatio); } g.DrawImage(_currentBitmap, margin, y, dW, dH); g.DrawRectangle(Pens.Black, margin, y, dW, dH); y += dH + 20; }
                        if (_specPlot.Image != null) { g.DrawString("2. Firmas Espectrales", fSub, Brushes.Black, margin, y); y += 25; int spW = width, spH = (int)((float)_specPlot.Image.Height / _specPlot.Image.Width * spW); if (spH > 220) { spH = 220; spW = (int)((float)spH / ((float)_specPlot.Image.Height / _specPlot.Image.Width)); } g.DrawImage(_specPlot.Image, margin, y, spW, spH); g.DrawRectangle(Pens.Black, margin, y, spW, spH); y += spH + 20; }
                        if (_selections.Count > 0) { g.DrawString("3. Datos de Regiones (Brix y Metadatos)", fSub, Brushes.Black, margin, y); y += 25; foreach (var roi in _selections) { using var b = new SolidBrush(roi.Color); g.FillRectangle(b, margin, y + 2, 12, 12); g.DrawRectangle(Pens.Black, margin, y + 2, 12, 12); string txt = $"{roi.ShortLabel}"; if (roi.MeasuredBrix.HasValue) txt += $" | Brix: {roi.MeasuredBrix.Value:F1}°"; if (!string.IsNullOrEmpty(roi.Variety)) txt += $" | Var: {roi.Variety}"; if (!string.IsNullOrEmpty(roi.Notes)) txt += $" | Notas: {roi.Notes}"; g.DrawString(txt, fNorm, Brushes.Black, margin + 20, y); y += 20; } y += 10; }
                        if (!string.IsNullOrEmpty(_txtAnalysisReport.Text)) { if (y > args.PageBounds.Height - 200) { args.HasMorePages = true; currentStep = 1; return; } else { g.DrawString("4. Resumen Quimiométrico (PCA)", fSub, Brushes.Black, margin, y); y += 25; var rect = new RectangleF(margin, y, width, args.PageBounds.Height - y - 50); g.DrawString(_txtAnalysisReport.Text, fMono, Brushes.DarkBlue, rect); } }
                        args.HasMorePages = false;
                    }
                    else if (currentStep == 1) { g.DrawString("4. Resumen Quimiométrico (PCA) - Continuación", fSub, Brushes.Black, margin, y); y += 25; var rect = new RectangleF(margin, y, width, args.PageBounds.Height - y - 50); g.DrawString(_txtAnalysisReport.Text, fMono, Brushes.DarkBlue, rect); args.HasMorePages = false; }
                }; pd.Print(); MessageBox.Show("Informe PDF generado exitosamente.", "Éxito", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show($"No se pudo generar el PDF.\nError: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { _slbl.Text = "Listo."; _pb.Visible = false; }
        }

        private async void BtnBatch_Click(object? s, EventArgs e)
        {
            // AÑADIDO: Si hay un cubo cargado, forzamos a que el usuario ajuste los parámetros primero
            SegmentationParams batchParams = new SegmentationParams();

            if (_cube != null)
            {
                var msgResult = MessageBox.Show(
                    "Se ha detectado una imagen abierta.\n\n¿Quieres usarla para ajustar los parámetros de segmentación antes de procesar el lote completo?\n\n(Recomendado para evitar carpetas vacías si el umbral por defecto falla)",
                    "Ajuste Previo", MessageBoxButtons.YesNo, MessageBoxIcon.Information);

                if (msgResult == DialogResult.Yes)
                {
                    using var dlg = new InteractiveSegmentationForm(_cube, _currentBand);
                    if (dlg.ShowDialog() != DialogResult.OK) return; // Si cancela, salimos
                    batchParams = dlg.Params; // Guardamos los parámetros que ha elegido el usuario
                }
            }

            using var fbd = new FolderBrowserDialog { Description = "Selecciona la carpeta con archivos crudos (.hdr)" };
            if (fbd.ShowDialog() != DialogResult.OK) return;

            using var sfd = new FolderBrowserDialog { Description = "Selecciona la carpeta de destino para las exportaciones" };
            if (sfd.ShowDialog() != DialogResult.OK) return;

            var result = MessageBox.Show(
                "¿Deseas activar la AUTO-SEGMENTACIÓN en este lote?\n\n" +
                "SÍ: Extraerá cada objeto por separado y guardará sus máscaras (.mat, ENVI, .npy).\n" +
                "NO: Extraerá solo el espectro global de cada imagen completa.",
                "Modo de Procesamiento Masivo",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

            if (result == DialogResult.Cancel) return;

            var batchOpts = new BatchOptions
            {
                ApplyNormalize = _stepNormalize,
                ConvertToAbsorbance = _stepAbsorbance,
                ApplySNV = _stepScatter == ScatterCorrection.SNV,
                ApplyMSC = _stepScatter == ScatterCorrection.MSC,
                ApplySavitzkyGolay = _stepSG,
                SgWindow = _sgWindow,
                SgPoly = _sgPoly,
                SgDeriv = _sgDeriv,
                ApplyMedianFilter = _stepMedian,
                AutoSegment = (result == DialogResult.Yes),
                SegmentationBand = _currentBand,
                SaveNpyMasks = (result == DialogResult.Yes),

                // AÑADIDO: Ahora pasamos los parámetros interactivos o los de por defecto
                CustomParams = batchParams
            };

            _cts?.Cancel(); _cts = new CancellationTokenSource(); var token = _cts.Token;

            _pb.Visible = true; _pb.Style = ProgressBarStyle.Continuous;
            _btnCancelTask.Visible = true; _btnCancelTask.Enabled = true; _btnCancelTask.Text = "🛑 Cancelar lote";
            _slbl.Text = "Procesando imágenes por lotes...";

            var progress = new Progress<int>(v => { _pb.Value = v; _slbl.Text = $"Procesando lote... {v}% completado"; });

            try
            {
                await BatchProcessor.ProcessFolderAsync(fbd.SelectedPath, sfd.SelectedPath, batchOpts, progress, token);
                MessageBox.Show("Procesamiento completado.\nSe han generado las exportaciones en la carpeta de destino.", "Éxito", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (OperationCanceledException) { MessageBox.Show("Cancelado por el usuario.", "Cancelado"); }
            catch (Exception ex) { MessageBox.Show($"Error Crítico:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { _pb.Visible = false; _btnCancelTask.Visible = false; _slbl.Text = "Procesamiento finalizado."; }
        }

        private async void BtnAutoSegment_Click(object? sender, EventArgs e)
        {
            if (_cube == null)
            {
                MessageBox.Show("Carga un cubo hiperespectral antes de iniciar la segmentación.", "Atención", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using var dlg = new InteractiveSegmentationForm(_cube, _currentBand);
            if (dlg.ShowDialog() != DialogResult.OK) return;

            var result = MessageBox.Show(
                "Ajustes confirmados.\n\n¿Deseas aplicar esta segmentación a TODAS las imágenes de una carpeta?\n(Se exportará en .mat, .npy, .csv y formato original ENVI)\n\nSí = Procesar Carpeta (Batch)\nNo = Solo imagen actual",
                "Modo de Procesamiento",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

            if (result == DialogResult.Cancel) return;

            if (result == DialogResult.No)
            {
                _slbl.Text = "Segmentando imagen actual...";
                _pb.Visible = true; _pb.Style = ProgressBarStyle.Marquee;

                try
                {
                    var rois = await AutoSegmenter.SegmentCubeAsync(_cube, _currentBand, dlg.Params, null, default);

                    if (rois.Count > 0)
                    {
                        SaveStateForUndo();
                        _selections.AddRange(rois);
                        _btnClear.Enabled = true;
                        RefreshDisplay();
                        _pictureBox.Invalidate();
                        _slbl.Text = $"✔ ULTRAVISOR: Detectados {rois.Count} objetos.";
                    }
                    else
                    {
                        _slbl.Text = "⚠ No se detectaron objetos con los parámetros actuales.";
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error en segmentación: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    _pb.Visible = false; _pb.Style = ProgressBarStyle.Continuous;
                }
            }
            else if (result == DialogResult.Yes)
            {
                using var fbd = new FolderBrowserDialog { Description = "Selecciona la carpeta de origen con las imágenes crudas (HDR/BIL)" };
                if (fbd.ShowDialog() != DialogResult.OK) return;

                using var sfd = new FolderBrowserDialog { Description = "Selecciona la carpeta de destino para guardar las exportaciones" };
                if (sfd.ShowDialog() != DialogResult.OK) return;

                var batchOpts = new BatchOptions
                {
                    AutoSegment = true,
                    SegmentationBand = _currentBand,
                    CustomParams = dlg.Params,
                    ApplyNormalize = _stepNormalize,
                    ConvertToAbsorbance = _stepAbsorbance,
                    ApplySNV = _stepScatter == ScatterCorrection.SNV,
                    ApplyMSC = _stepScatter == ScatterCorrection.MSC,
                    ApplySavitzkyGolay = _stepSG,
                    SgWindow = _sgWindow,
                    SgPoly = _sgPoly,
                    SgDeriv = _sgDeriv,
                    ApplyMedianFilter = _stepMedian
                };

                string originalPipelineText = _lblPipeline.Text;
                _cts?.Cancel();
                _cts = new CancellationTokenSource();
                var token = _cts.Token;

                var progress = new Progress<int>(percent =>
                {
                    _lblPipeline.Text = $"{originalPipelineText} | ⏳ EXPORTANDO: {percent}%";
                    _pb.Value = percent;
                    _pb.Visible = true;
                    _slbl.Text = $"Batch: {percent}% completado";
                });

                try
                {
                    _btnCancelTask.Visible = true;
                    _btnCancelTask.Enabled = true;
                    _btnCancelTask.Text = "🛑 Cancelar Batch";

                    await BatchProcessor.ProcessFolderAsync(fbd.SelectedPath, sfd.SelectedPath, batchOpts, progress, token);

                    MessageBox.Show("Batch completado con éxito.\nSe han generado subcarpetas con los formatos .mat, ENVI, .npy y el CSV de espectros.", "Finalizado", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (OperationCanceledException)
                {
                    MessageBox.Show("Proceso cancelado por el usuario.", "Cancelado");
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error en el proceso batch:\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    _lblPipeline.Text = originalPipelineText;
                    _pb.Visible = false;
                    _btnCancelTask.Visible = false;
                    _slbl.Text = "Listo.";
                }
            }
        }

        private void BtnAnalyzeFolder_Click(object? sender, EventArgs e)
        {
            using var fbd = new FolderBrowserDialog { Description = "Selecciona la carpeta con las imágenes autosegmentadas (.hdr/.bil)" };
            if (fbd.ShowDialog() == DialogResult.OK)
            {
                var galleryForm = new BatchReviewForm(fbd.SelectedPath);

                galleryForm.OnCollageCreated += (s, collageCube) =>
                {
                    _originalCube = null; _baseCube = null; _cube = null;
                    _selections.Clear();
                    _undoStack.Clear(); _redoStack.Clear(); UpdateUndoRedoUI();

                    _baseCube = collageCube;
                    _originalCube = _baseCube.Clone();
                    _cube = _baseCube;

                    _loadedFileName = "Collage_Multimuestra";
                    string currentCam = _cmbCamera.SelectedItem.ToString();
                    this.Text = $"Specimen — Workspace [{_loadedFileName}] ({currentCam})";

                    _currentBand = 0;
                    PopulateBandsCombo();
                    _slider.Minimum = 0; _slider.Maximum = Math.Max(0, _cube.Bands - 1); _slider.Value = 0;

                    CheckCalibrationReady();
                    _btnExport.Enabled = _btnExpAll.Enabled = _btnExpMeanSpec.Enabled = _btnExpGraph.Enabled = _btnReport.Enabled = _btnClose.Enabled = true;

                    _zoomFactor = 1.0f; _panOffset = new PointF(0, 0);
                    RefreshDisplay();
                    ClearSpectrumPlot();
                    _slbl.Text = $"✔ Collage Cargado: {_cube.Samples}px de ancho x {_cube.Lines}px de alto.";
                };

                OpenChildForm(galleryForm);
            }
        }

        private async void LoadCubeFromFile(string filePath)
        {
            if (_estaCargando) return;
            _estaCargando = true;

            try
            {
                _btnLoad.Enabled = false; _pb.Visible = true; _pb.Value = 0; _slbl.Text = "Cargando cubo...";
                var prog = new Progress<int>(v => { _pb.Value = v; _slbl.Text = $"Cargando… {v} %"; });

                _baseCube = await Task.Run(() => HyperspectralCube.Load(filePath, prog));
                _originalCube = _baseCube.Clone(); _cube = _baseCube; _selections.Clear(); _chkAnalyze.Checked = false;

                _undoStack.Clear(); _redoStack.Clear(); UpdateUndoRedoUI();

                _currentBand = 0;
                PopulateBandsCombo();
                _slider.Minimum = 0; _slider.Maximum = Math.Max(0, _cube.Bands - 1); _slider.Value = 0;

                _loadedFileName = Path.GetFileName(filePath);

                string currentCam = _cmbCamera.SelectedItem.ToString();
                this.Text = $"Specimen — Workspace [{_loadedFileName}] ({currentCam})";

                CheckCalibrationReady();
                _btnExport.Enabled = _btnExpAll.Enabled = _btnExpMeanSpec.Enabled = _btnExpGraph.Enabled = _btnReport.Enabled = _btnClose.Enabled = true;

                _zoomFactor = 1.0f; _panOffset = new PointF(0, 0);

                RefreshDisplay(); ClearSpectrumPlot(); _slbl.Text = $"✔ {_cube.Header}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudo leer el archivo.\n\nDetalle: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _slbl.Text = "Error";
            }
            finally
            {
                _pb.Visible = false;
                _btnLoad.Enabled = true;
                _estaCargando = false;
            }
        }

        private void SetTool(SelectionTool mode, string tip)
        {
            if (_polyActive) { _polyActive = false; _polyImg.Clear(); _polyScr.Clear(); _pictureBox.Invalidate(); }
            _tool = mode; _lblTip.Text = tip;
            for (int i = 0; i < _toolBtns.Length; i++) { var (m, _) = ((SelectionTool, string))_toolBtns[i].Tag!; _toolBtns[i].BackColor = m == mode ? Color.FromArgb(0, 122, 204) : Color.FromArgb(45, 45, 48); }
            _pictureBox.Cursor = mode switch { SelectionTool.Polygon => Cursors.UpArrow, SelectionTool.Freehand => Cursors.UpArrow, SelectionTool.AutoDetect => Cursors.Hand, _ => Cursors.Cross };
        }

        private void CheckCalibrationReady() => _btnCalibrate.Enabled = _originalCube != null && _whiteCube != null && _darkCube != null;

        private async Task RebuildWorkingCube()
        {
            if (_originalCube == null) return;

            _cts?.Cancel(); _cts = new CancellationTokenSource(); var token = _cts.Token;

            _slbl.Text = "Reconstruyendo pipeline desde el original...";
            _pb.Visible = true; _pb.Style = ProgressBarStyle.Marquee;
            _btnCancelTask.Visible = true; _btnCancelTask.Enabled = true; _btnCancelTask.Text = "🛑 Cancelar proceso";

            Invoke(() => { foreach (var f in _childForms.ToList()) ((Form)f).Close(); });

            try
            {
                await Task.Run(() =>
                {
                    _baseCube = _originalCube.Clone();
                    if (_stepNormalize && _whiteCube != null && _darkCube != null)
                    {
                        try { _baseCube.Calibrate(_whiteCube, _darkCube, token); }
                        catch (ArgumentException ex) { Invoke(() => MessageBox.Show($"Conflicto:\n{ex.Message}", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning)); _stepNormalize = false; _stepAbsorbance = false; }
                    }
                    if (_stepRotation != 0f) _baseCube.ApplySpatialRotation(_stepRotation, token);
                    if (_stepAbsorbance && _baseCube.IsCalibrated) _baseCube.ConvertToAbsorbance(token);
                    if (_stepScatter == ScatterCorrection.SNV) _baseCube.ApplySNV(token);
                    else if (_stepScatter == ScatterCorrection.MSC) _baseCube.ApplyMSC(token);
                    if (_stepSG) _baseCube.ApplySavitzkyGolay(_sgWindow, _sgPoly, _sgDeriv, token);
                    if (_stepMedian) _baseCube.ApplySpatialMedianFilter(3, token);
                }, token);

                UpdatePipelineLabel();
                if (_chkAnalyze.Checked) await RunAnalysisAsync(); else { _cube = _baseCube; RefreshDisplay(); }
                ClearSpectrumPlot(); _slbl.Text = "Pipeline aplicado.";
            }
            catch (OperationCanceledException)
            {
                _slbl.Text = "Operación cancelada. Restaurando imagen original...";
                _cube = _originalCube.Clone(); _baseCube = _cube; ResetPipelineUI(); RefreshDisplay();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Error"); _slbl.Text = "Error al reconstruir pipeline."; }
            finally { _pb.Visible = false; _pb.Style = ProgressBarStyle.Continuous; _btnCancelTask.Visible = false; }
        }

        private void UpdatePipelineLabel()
        {
            if (_lblPipeline == null) return;
            var steps = new System.Collections.Generic.List<string> { "Orig" };
            if (_stepRotation != 0f) steps.Add($"Rot({_stepRotation}º)");
            if (_stepNormalize) steps.Add("Norm");
            if (_stepAbsorbance) steps.Add("Abs");
            if (_stepScatter == ScatterCorrection.SNV) steps.Add("SNV"); else if (_stepScatter == ScatterCorrection.MSC) steps.Add("MSC");
            if (_stepSG) steps.Add($"SG");
            if (_stepMedian) steps.Add("Med");
            _lblPipeline.Text = "Pipeline: " + string.Join("→", steps);
        }

        private static void UpdateToggleButton(Button btn, bool active, Color baseColor)
        {
            btn.BackColor = active ? Color.FromArgb(Math.Min(255, baseColor.R + 60), Math.Min(255, baseColor.G + 60), Math.Min(255, baseColor.B + 60)) : baseColor;
            btn.FlatAppearance.BorderColor = active ? Color.FromArgb(100, 220, 120) : Color.FromArgb(70, 70, 100);
        }

        private async Task RunAnalysisAsync()
        {
            if (_baseCube == null) return;

            _cts?.Cancel(); _cts = new CancellationTokenSource(); var token = _cts.Token;

            _slbl.Text = "Calculando PCA... (Esto puede tardar)";
            _pb.Visible = true; _pb.Style = ProgressBarStyle.Marquee; _chkAnalyze.Enabled = false;
            _btnCancelTask.Visible = true; _btnCancelTask.Enabled = true; _btnCancelTask.Text = "🛑 Cancelar análisis";

            try
            {
                await Task.Run(() => {
                    bool[,] mask = new bool[_baseCube.Lines, _baseCube.Samples];
                    if (_selections.Count > 0) { foreach (var sh in _selections) { var m = sh.GetMask(_baseCube.Lines, _baseCube.Samples); for (int l = 0; l < _baseCube.Lines; l++) for (int c = 0; c < _baseCube.Samples; c++) if (m[l, c]) mask[l, c] = true; } }
                    else { int xMin = _baseCube.Samples < 1000 ? 40 : 200, xMax = _baseCube.Samples - (_baseCube.Samples < 1000 ? 60 : 200), yMin = _baseCube.Lines < 600 ? 70 : 300, yMax = _baseCube.Lines - (_baseCube.Lines < 600 ? 40 : 100); for (int l = 0; l < _baseCube.Lines; l++) for (int s = 0; s < _baseCube.Samples; s++) mask[l, s] = (l >= yMin && l <= yMax && s >= xMin && s <= xMax); }
                    _cube = _baseCube.GenerateAnalyzedCube(10, mask, token);
                }, token);

                _slbl.Text = "Análisis completado.";
                if (_cube != null) _txtAnalysisReport.Text = _cube.AnalysisReport;
                PopulateBandsCombo(); _slider.Maximum = _cube!.Bands - 1; if (_currentBand >= _cube.Bands) _currentBand = _cube.Bands - 1; _slider.Value = _currentBand; _cmbBands.SelectedIndex = _currentBand; RefreshDisplay();
            }
            catch (OperationCanceledException) { _slbl.Text = "Análisis PCA cancelado por el usuario."; _chkAnalyze.Checked = false; }
            finally { _pb.Visible = false; _pb.Style = ProgressBarStyle.Continuous; _chkAnalyze.Enabled = true; _btnCancelTask.Visible = false; }
        }

        private async void ChkAnalyze_CheckedChanged(object? s, EventArgs e)
        {
            if (_baseCube == null) return;
            if (_chkAnalyze.Checked) await RunAnalysisAsync(); else { _cube = _baseCube; _slbl.Text = "Restaurado cubo original."; _txtAnalysisReport.Text = ""; PopulateBandsCombo(); _slider.Maximum = _cube!.Bands - 1; if (_currentBand >= _cube.Bands) _currentBand = _cube.Bands - 1; _slider.Value = _currentBand; _cmbBands.SelectedIndex = _currentBand; RefreshDisplay(); }
        }

        private void PopulateBandsCombo()
        {
            var currentCube = _cube ?? _baseCube;
            _cmbBands.Items.Clear(); if (currentCube == null) return;

            int origBands = _baseCube != null ? _baseCube.Header.Bands : currentCube.Header.Bands;
            for (int i = 0; i < origBands; i++) { double wl = currentCube.Header.Wavelengths != null && currentCube.Header.Wavelengths.Count > i ? currentCube.Header.Wavelengths[i] : i; _cmbBands.Items.Add($"Banda {i + 1} - {wl:F1} nm"); }

            if (currentCube.Bands > origBands) { _cmbBands.Items.Add("Media"); _cmbBands.Items.Add("Mínima"); _cmbBands.Items.Add("Máxima"); _cmbBands.Items.Add("Rango"); int numPca = currentCube.Bands - origBands - 4; for (int i = 0; i < numPca; i++) _cmbBands.Items.Add($"PC {i + 1}"); }

            _slider.Maximum = Math.Max(0, currentCube.Bands - 1);
            if (_cmbBands.Items.Count > 0) _cmbBands.SelectedIndex = Clamp(_currentBand, 0, _cmbBands.Items.Count - 1);
        }

        private bool[,]? GetCurrentMask()
        {
            var currentCube = _cube;
            if (currentCube == null || _selections.Count == 0) return null;
            int w = currentCube.Samples, h = currentCube.Lines;
            bool[,] mask = new bool[h, w];
            bool hasAny = false;
            foreach (var sh in _selections)
            {
                var m = sh.GetMask(h, w);
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) if (m[y, x]) { mask[y, x] = true; hasAny = true; }
            }
            return hasAny ? mask : null;
        }

        private (float min, float max) GetMaskedStats(int band, bool[,] mask)
        {
            var currentCube = _cube!;
            float min = float.MaxValue, max = float.MinValue;
            for (int y = 0; y < currentCube.Lines; y++)
            {
                for (int x = 0; x < currentCube.Samples; x++)
                {
                    if (mask[y, x])
                    {
                        float v = currentCube[band, y, x];
                        if (!float.IsNaN(v) && !float.IsInfinity(v))
                        {
                            if (v < min) min = v;
                            if (v > max) max = v;
                        }
                    }
                }
            }
            if (min == float.MaxValue) return (0, 1);
            return (min, max);
        }

        private static (byte R, byte G, byte B) GetColor(float t, BliColormap map)
        {
            float r, g, b;
            switch (map)
            {
                case BliColormap.HeatMap: return (ToByte(Clamp(t * 3f, 0, 1)), ToByte(Clamp(t * 3f - 1f, 0, 1)), ToByte(Clamp(t * 3f - 2f, 0, 1)));
                case BliColormap.Grayscale: return (ToByte(t), ToByte(t), ToByte(t));
                case BliColormap.ColdBlue: return (ToByte(Clamp(t * 2 - 1, 0, 1)), ToByte(Clamp(t * 2 - 1, 0, 1)), ToByte(Clamp(t * 2, 0, 1)));
                case BliColormap.GreenFluorescent: return (ToByte(Clamp(t * 2 - 1, 0, 1) * 0.5f), ToByte(Clamp(t * 1.5f, 0, 1)), ToByte(Clamp(t * 0.5f, 0, 1)));
                case BliColormap.RedFluorescent: return (ToByte(Clamp(t * 1.5f, 0, 1)), ToByte(Clamp(t * 0.5f, 0, 1) * 0.3f), 0);
                default:
                    if (t < 0.125f) { r = 0; g = 0; b = 0.5f + t * 4f; } else if (t < 0.375f) { r = 0; g = (t - .125f) * 4f; b = 1f; } else if (t < 0.625f) { r = (t - .375f) * 4f; g = 1f; b = 1f - (t - .375f) * 4f; } else if (t < 0.875f) { r = 1f; g = 1f - (t - .625f) * 4f; b = 0f; } else { r = 1f; g = (t - .875f) * 8f; b = (t - .875f) * 8f; }
                    return (ToByte(r), ToByte(g), ToByte(b));
            }
        }
        private static byte ToByte(float v) => (byte)(Clamp(v, 0f, 1f) * 255f);

        private Bitmap RenderMaskedBand(int band, bool[,] mask, float min, float max)
        {
            var currentCube = _cube!;
            int w = currentCube.Samples, h = currentCube.Lines;
            var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            var bData = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            BliColormap cmap = _grayscaleMode ? BliColormap.Grayscale : (BliColormap)_cmbCmap.SelectedIndex;

            try
            {
                int stride = bData.Stride;
                float range = max - min; if (range < 1e-10f) range = 1f;

                var (gMin, gMax) = currentCube.GetBandStats(band); float gRng = gMax - gMin; if (gRng < 1e-10f) gRng = 1f;

                for (int y = 0; y < h; y++)
                {
                    byte[] rowPixels = new byte[stride];
                    for (int x = 0; x < w; x++)
                    {
                        float v = currentCube[band, y, x]; int offset = x * 3;
                        if (mask[y, x])
                        {
                            float t = float.IsNaN(v) ? 0f : Clamp((v - min) / range, 0f, 1f);
                            var (r, g, b) = GetColor(t, cmap);
                            rowPixels[offset] = b; rowPixels[offset + 1] = g; rowPixels[offset + 2] = r;
                        }
                        else
                        {
                            float t = float.IsNaN(v) ? 0f : Clamp((v - gMin) / gRng, 0f, 1f);
                            byte gray = (byte)(t * 255 * 0.25f);
                            rowPixels[offset] = gray; rowPixels[offset + 1] = gray; rowPixels[offset + 2] = gray;
                        }
                    }
                    Marshal.Copy(rowPixels, 0, bData.Scan0 + y * stride, stride);
                }
            }
            finally
            {
                bmp.UnlockBits(bData);
            }

            if (_chkCbar.Checked)
            {
                using var g = Graphics.FromImage(bmp);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int barH = Math.Min(120, bmp.Height - 30), barW = 14;
                int bx = bmp.Width - barW - 8, by = 15;

                for (int i = 0; i < barH; i++)
                {
                    float t = 1f - (float)i / barH;
                    var (r, gc, b) = GetColor(t, cmap);
                    using var pen = new Pen(Color.FromArgb(r, gc, b));
                    g.DrawLine(pen, bx, by + i, bx + barW, by + i);
                }
                g.DrawRectangle(Pens.White, bx, by, barW, barH);
                using var font = new Font("Arial", 7f);
                g.DrawString(max.ToString("G4"), font, Brushes.White, bx - 2, by - 1);
                g.DrawString(min.ToString("G4"), font, Brushes.White, bx - 2, by + barH + 1);
            }

            return bmp;
        }

        private Bitmap RenderMaskedRGB(int bR, int bG, int bB, bool[,] mask, float minR, float maxR, float minG, float maxG, float minB, float maxB)
        {
            var currentCube = _cube!;
            int w = currentCube.Samples, h = currentCube.Lines;
            var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            var bData = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

            try
            {
                int stride = bData.Stride;
                float rngR = maxR - minR; if (rngR < 1e-10f) rngR = 1f; float rngG = maxG - minG; if (rngG < 1e-10f) rngG = 1f; float rngB = maxB - minB; if (rngB < 1e-10f) rngB = 1f;
                var (gMinR, gMaxR) = currentCube.GetBandStats(bR); float gRngR = gMaxR - gMinR; if (gRngR == 0) gRngR = 1;
                var (gMinG, gMaxG) = currentCube.GetBandStats(bG); float gRngG = gMaxG - gMinG; if (gRngG == 0) gRngG = 1;
                var (gMinB, gMaxB) = currentCube.GetBandStats(bB); float gRngB = gMaxB - gMinB; if (gRngB == 0) gRngB = 1;

                for (int y = 0; y < h; y++)
                {
                    byte[] rowPixels = new byte[stride];
                    for (int x = 0; x < w; x++)
                    {
                        float vR = currentCube[bR, y, x], vG = currentCube[bG, y, x], vB = currentCube[bB, y, x]; int offset = x * 3;
                        if (mask[y, x])
                        {
                            rowPixels[offset] = float.IsNaN(vB) ? (byte)0 : (byte)(Clamp((vB - minB) / rngB, 0f, 1f) * 255);
                            rowPixels[offset + 1] = float.IsNaN(vG) ? (byte)0 : (byte)(Clamp((vG - minG) / rngG, 0f, 1f) * 255);
                            rowPixels[offset + 2] = float.IsNaN(vR) ? (byte)0 : (byte)(Clamp((vR - minR) / rngR, 0f, 1f) * 255);
                        }
                        else
                        {
                            float tR = float.IsNaN(vR) ? 0f : Clamp((vR - gMinR) / gRngR, 0f, 1f);
                            float tG = float.IsNaN(vG) ? 0f : Clamp((vG - gMinG) / gRngG, 0f, 1f);
                            float tB = float.IsNaN(vB) ? 0f : Clamp((vB - gMinB) / gRngB, 0f, 1f);
                            byte gray = (byte)(((tR + tG + tB) / 3f) * 255 * 0.25f);
                            rowPixels[offset] = gray; rowPixels[offset + 1] = gray; rowPixels[offset + 2] = gray;
                        }
                    }
                    Marshal.Copy(rowPixels, 0, bData.Scan0 + y * stride, stride);
                }
            }
            finally
            {
                bmp.UnlockBits(bData);
            }
            return bmp;
        }

        private void RefreshDisplay()
        {
            var currentCube = _cube;
            if (currentCube == null) return;
            string bandName = _cmbBands.Items.Count > _currentBand ? _cmbBands.Items[_currentBand].ToString()! : $"Banda {_currentBand + 1}";
            bool isPcaBand = _currentBand >= (_baseCube != null ? _baseCube.Header.Bands : currentCube.Header.Bands) + 4;
            Bitmap? newBitmap;
            bool[,] mask = GetCurrentMask()!;

            string resText = "";
            if (currentCube.Header.Wavelengths != null && currentCube.Header.Wavelengths.Count > 1)
            {
                double rangoNm = currentCube.Header.Wavelengths.Last() - currentCube.Header.Wavelengths.First();
                double resolucion = rangoNm / currentCube.Bands;
                resText = $"\nRes. Espectral: ~{resolucion:F2} nm/px";
            }

            if (mask != null && !isPcaBand)
            {
                if (_rgbMode)
                {
                    int bR = GetClosestBand(640), bG = GetClosestBand(550), bB = GetClosestBand(460);
                    var (minR, maxR) = GetMaskedStats(bR, mask); var (minG, maxG) = GetMaskedStats(bG, mask); var (minB, maxB) = GetMaskedStats(bB, mask);
                    newBitmap = RenderMaskedRGB(bR, bG, bB, mask, minR, maxR, minG, maxG, minB, maxB);
                    _lblBandInfo.Text = $"Modo RGB (ROI Aislado)\nR: {WlAt(bR):F1} nm\nG: {WlAt(bG):F1} nm\nB: {WlAt(bB):F1} nm\nPx: {currentCube.Samples}x{currentCube.Lines}{resText}";
                }
                else
                {
                    var (mn, mx) = GetMaskedStats(_currentBand, mask);
                    newBitmap = RenderMaskedBand(_currentBand, mask, mn, mx);
                    _lblBandInfo.Text = $"{bandName} (ROI Aislado)\nMín (ROI): {mn:G5}\nMáx (ROI): {mx:G5}\nPx: {currentCube.Samples}x{currentCube.Lines}{resText}";
                }
            }
            else
            {
                var opts = new BliRenderOptions { Colormap = isPcaBand || _grayscaleMode ? BliColormap.Grayscale : (BliColormap)_cmbCmap.SelectedIndex, Gamma = isPcaBand ? 1.0f : (float)_nudGamma.Value, LowPercentile = isPcaBand ? 0f : (float)_nudLo.Value, HighPercentile = isPcaBand ? 100f : (float)_nudHi.Value, SignalThreshold = isPcaBand ? 0f : (float)_nudThr.Value, DrawColorbar = _chkCbar.Checked && !_rgbMode, Wavelength = WlAt(_currentBand), WavelengthUnit = currentCube.Header.WavelengthUnits };
                if (_rgbMode)
                {
                    int bR = GetClosestBand(640), bG = GetClosestBand(550), bB = GetClosestBand(460);
                    newBitmap = BliRenderer.RenderRGB(currentCube, bR, bG, bB, opts);
                    _lblBandInfo.Text = $"Modo RGB\nR: {WlAt(bR):F1} nm\nG: {WlAt(bG):F1} nm\nB: {WlAt(bB):F1} nm\nPx: {currentCube.Samples}x{currentCube.Lines}{resText}";
                }
                else
                {
                    newBitmap = BliRenderer.RenderBand(currentCube, _currentBand, opts);
                    var (mn, mx) = currentCube.GetBandStats(_currentBand);
                    _lblBandInfo.Text = $"{bandName}\nMín Global: {mn:G5}\nMáx Global: {mx:G5}\nPx: {currentCube.Samples}x{currentCube.Lines}{resText}";
                }
            }

            Bitmap? oldBitmap = _currentBitmap;
            _currentBitmap = newBitmap;

            _pictureBox.Image = _currentBitmap;
            oldBitmap?.Dispose();

            _pictureBox.Invalidate();
            RedrawSpectrumPlot();
        }

        private int GetClosestBand(double targetWl) { if (_cube == null || _cube.Header.Wavelengths == null || _cube.Header.Wavelengths.Count == 0) return 0; return _cube.Header.Wavelengths.Select((wl, i) => (diff: Math.Abs(wl - targetWl), i)).OrderBy(x => x.diff).First().i; }
        private void ClearSpectrumPlot() { _specPlot.Image?.Dispose(); _specPlot.Image = null; if (_cube != null) RefreshDisplay(); }

        private void RedrawSpectrumPlot()
        {
            var currentCube = _cube;
            if (currentCube == null || (_selections.Count == 0 && _hoverImgPt == null)) return;
            int w = Math.Max(_specPlot.Width, 300), h = Math.Max(_specPlot.Height, 80); var bmp = new Bitmap(w, h); using var g = Graphics.FromImage(bmp); g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.FromArgb(12, 12, 20));
            const int pL = 64, pR = 20, pT = 24, pB = 40; var plot = new Rectangle(pL, pT, w - pL - pR, h - pT - pB); if (plot.Width < 20 || plot.Height < 10) { _specPlot.Image = bmp; return; }
            int plotBands = _baseCube != null ? _baseCube.Header.Bands : currentCube.Header.Bands;

            float yMin = float.MaxValue, yMax = float.MinValue;
            foreach (var sh in _selections) foreach (float v in sh.GetSpectrum(currentCube).Take(plotBands)) { if (!float.IsNaN(v) && !float.IsInfinity(v) && v < yMin) yMin = v; if (!float.IsNaN(v) && !float.IsInfinity(v) && v > yMax) yMax = v; }
            float[]? hoverSpec = null; if (_hoverImgPt.HasValue) { int hx = _hoverImgPt.Value.X, hy = _hoverImgPt.Value.Y; if (hx >= 0 && hx < currentCube.Samples && hy >= 0 && hy < currentCube.Lines) { hoverSpec = currentCube.GetSpectrum(hy, hx).Take(plotBands).ToArray(); foreach (float v in hoverSpec) { if (!float.IsNaN(v) && !float.IsInfinity(v) && v < yMin) yMin = v; if (!float.IsNaN(v) && !float.IsInfinity(v) && v > yMax) yMax = v; } } }
            if (yMin == float.MaxValue) { yMin = 0; yMax = 1; }
            float yRng = yMax - yMin; if (yRng < 1e-10f) yRng = 1f; yMin -= yRng * 0.05f; yMax += yRng * 0.05f; yRng = yMax - yMin;
            var wls = currentCube.Header.Wavelengths;
            double xMin = wls != null && wls.Count > 0 ? wls[0] : 0, xMax = wls != null && wls.Count > 0 ? wls[^1] : plotBands - 1, xRng = xMax - xMin;
            if (xRng == 0) xRng = 1;

            using (var gp = new Pen(Color.FromArgb(28, 255, 255, 255), 1f) { DashStyle = DashStyle.Dash }) { for (int i = 0; i <= 5; i++) g.DrawLine(gp, plot.Left, plot.Bottom - (float)i / 5 * plot.Height, plot.Right, plot.Bottom - (float)i / 5 * plot.Height); for (int i = 0; i <= 6; i++) g.DrawLine(gp, plot.Left + (float)i / 6 * plot.Width, plot.Top, plot.Left + (float)i / 6 * plot.Width, plot.Bottom); }
            g.DrawRectangle(new Pen(Color.FromArgb(65, 255, 255, 255)), plot);
            if (_currentBand < plotBands) { double curWl = WlAt(_currentBand); float curPx = plot.Left + (float)((curWl - xMin) / xRng * plot.Width); g.DrawLine(new Pen(Color.FromArgb(110, 255, 255, 80), 1f) { DashStyle = DashStyle.Dash }, curPx, plot.Top, curPx, plot.Bottom); }

            if (hoverSpec != null && hoverSpec.Length > 1)
            {
                var hp = new List<PointF>();
                for (int i = 0; i < hoverSpec.Length; i++)
                {
                    if (float.IsNaN(hoverSpec[i]) || float.IsInfinity(hoverSpec[i])) continue;
                    float px = plot.Left + (float)(((wls != null && i < wls.Count ? wls[i] : xMin + i * xRng / hoverSpec.Length) - xMin) / xRng * plot.Width);
                    float py = Clamp(plot.Bottom - (hoverSpec[i] - yMin) / yRng * plot.Height, plot.Top - 8, plot.Bottom + 8);
                    hp.Add(new PointF(px, py));
                }
                if (hp.Count > 1)
                {
                    using var hpen = new Pen(Color.FromArgb(180, 200, 200, 220), 1.5f) { DashStyle = DashStyle.Dot };
                    g.DrawLines(hpen, hp.ToArray());
                }
            }

            foreach (var sh in _selections)
            {
                var spec = sh.GetSpectrum(currentCube).Take(plotBands).ToArray();
                if (spec.Length > 1)
                {
                    var pts = new List<PointF>();
                    for (int i = 0; i < spec.Length; i++)
                    {
                        if (float.IsNaN(spec[i]) || float.IsInfinity(spec[i])) continue;
                        float px = plot.Left + (float)(((wls != null && i < wls.Count ? wls[i] : xMin + i * xRng / spec.Length) - xMin) / xRng * plot.Width);
                        float py = Clamp(plot.Bottom - (spec[i] - yMin) / yRng * plot.Height, plot.Top - 8, plot.Bottom + 8);
                        pts.Add(new PointF(px, py));
                    }
                    if (pts.Count > 1)
                    {
                        using var pen = new Pen(sh.Color, 1.8f);
                        g.DrawLines(pen, pts.ToArray());
                    }
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

        private void BtnExport_Click(object? s, EventArgs e) { if (_currentBitmap == null) return; using var dlg = new SaveFileDialog { Filter = "PNG (*.png)|*.png", FileName = $"Vista_{(_rgbMode ? "RGB" : $"banda{_currentBand + 1}_{WlAt(_currentBand):F1}nm")}" }; if (dlg.ShowDialog() == DialogResult.OK) { var bmpToSave = new Bitmap(_pictureBox.Width, _pictureBox.Height); _pictureBox.DrawToBitmap(bmpToSave, new Rectangle(0, 0, _pictureBox.Width, _pictureBox.Height)); bmpToSave.Save(dlg.FileName, ImageFormat.Png); } }

        private async void BtnExportAll_Click(object? s, EventArgs e)
        {
            var currentCube = _cube;
            if (currentCube == null) return;
            using var dlg = new FolderBrowserDialog(); if (dlg.ShowDialog() != DialogResult.OK) return;

            _cts?.Cancel(); _cts = new CancellationTokenSource(); var token = _cts.Token;

            _btnExpAll.Enabled = false;
            _pb.Visible = true; _pb.Value = 0; _pb.Style = ProgressBarStyle.Continuous;
            _btnCancelTask.Visible = true; _btnCancelTask.Enabled = true; _btnCancelTask.Text = "🛑 Cancelar exp.";
            _slbl.Text = "Exportando bandas...";

            try
            {
                await Task.Run(() => {
                    for (int b = 0; b < currentCube.Bands; b++)
                    {
                        token.ThrowIfCancellationRequested();
                        var o = new BliRenderOptions { Wavelength = WlAt(b), Colormap = _grayscaleMode ? BliColormap.Grayscale : BliColormap.Rainbow };
                        using var bmp = BliRenderer.RenderBand(currentCube, b, o);
                        bmp.Save(Path.Combine(dlg.SelectedPath, $"b_{b + 1:D3}.png"), System.Drawing.Imaging.ImageFormat.Png);
                        Invoke(() => _pb.Value = (b + 1) * 100 / currentCube.Bands);
                        if (b % 15 == 0) GC.Collect();
                    }
                }, token);
                _slbl.Text = "Exportación completada.";
            }
            catch (OperationCanceledException) { _slbl.Text = "Exportación cancelada."; }
            finally { _btnExpAll.Enabled = true; _pb.Visible = false; _btnCancelTask.Visible = false; }
        }

        private void BtnExpMeanSpec_Click(object? s, EventArgs e)
        {
            if (_cube == null) return;

            if (_selections.Count == 0)
            {
                MessageBox.Show("No has seleccionado ningún ROI. Dibuja al menos una región (rectángulo, polígono, etc.) en la imagen para exportar su espectro medio.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var dlg = new SaveFileDialog { Filter = "Archivo CSV (*.csv)|*.csv", FileName = $"Espectros_ROIs_{Path.GetFileNameWithoutExtension(_loadedFileName)}.csv", Title = "Guardar Espectros de ROIs" };

            if (dlg.ShowDialog() == DialogResult.OK)
            {
                var wls = _cube.Header.Wavelengths;
                int numBands = _cube.Bands;

                var roiSpectra = new List<float[]>();
                var roiNames = new List<string>();

                foreach (var sh in _selections)
                {
                    roiSpectra.Add(sh.GetSpectrum(_cube).ToArray());
                    roiNames.Add(sh.ShortLabel ?? "ROI");
                }

                var sb = new System.Text.StringBuilder();

                string header = "Banda,Longitud_Onda_nm";
                for (int i = 0; i < roiNames.Count; i++) header += $",{roiNames[i].Replace(",", "")}";
                sb.AppendLine(header);

                for (int i = 0; i < numBands; i++)
                {
                    double wl = (wls != null && wls.Count > i) ? wls[i] : i;
                    string line = $"{i + 1},{wl.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}";

                    foreach (var spec in roiSpectra)
                    {
                        float val = (i < spec.Length) ? spec[i] : 0f;
                        line += $",{val.ToString("G5", System.Globalization.CultureInfo.InvariantCulture)}";
                    }
                    sb.AppendLine(line);
                }

                File.WriteAllText(dlg.FileName, sb.ToString(), System.Text.Encoding.UTF8);
                MessageBox.Show("Los espectros medios de tus ROIs se han exportado correctamente a CSV.", "Exportación exitosa", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void BtnExpGraph_Click(object? s, EventArgs e)
        {
            if (_specPlot.Image == null)
            {
                MessageBox.Show("Selecciona algún punto en la imagen, carga una ROI o pasa el ratón sobre la muestra primero.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using var dlg = new SaveFileDialog { Filter = "Imagen PNG (*.png)|*.png", FileName = $"GraficaEspectral_{Path.GetFileNameWithoutExtension(_loadedFileName)}.png" };

            if (dlg.ShowDialog() == DialogResult.OK)
            {
                _specPlot.Image.Save(dlg.FileName, ImageFormat.Png);
                MessageBox.Show("Gráfica inferior exportada con éxito.", "Éxito", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        // --- MOTOR DE COORDENADAS REVISADO PARA ZOOM Y PAN ---
        private Point? MapToImage(Point sc)
        {
            if (_currentBitmap == null || _cube == null) return null;

            float ratioX = (float)_pictureBox.Width / _currentBitmap.Width;
            float ratioY = (float)_pictureBox.Height / _currentBitmap.Height;
            float baseScale = Math.Min(ratioX, ratioY);

            float itemWidth = _currentBitmap.Width * baseScale;
            float itemHeight = _currentBitmap.Height * baseScale;
            float offsetX = (_pictureBox.Width - itemWidth) / 2f;
            float offsetY = (_pictureBox.Height - itemHeight) / 2f;

            float x = (sc.X - offsetX - _panOffset.X) / (baseScale * _zoomFactor);
            float y = (sc.Y - offsetY - _panOffset.Y) / (baseScale * _zoomFactor);

            int imgX = (int)Math.Floor(x);
            int imgY = (int)Math.Floor(y);

            if (imgX < 0 || imgX >= _cube.Samples || imgY < 0 || imgY >= _cube.Lines)
                return null;

            return new Point(imgX, imgY);
        }

        // --- SISTEMA DE DIBUJO SIN STACKOVERFLOW ---
        private void Pic_Paint(object? s, PaintEventArgs e)
        {
            if (_currentBitmap == null) return;

            var g = e.Graphics;
            g.Clear(Color.Black);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;

            float ratioX = (float)_pictureBox.Width / _currentBitmap.Width;
            float ratioY = (float)_pictureBox.Height / _currentBitmap.Height;
            float baseScale = Math.Min(ratioX, ratioY);

            float itemWidth = _currentBitmap.Width * baseScale;
            float itemHeight = _currentBitmap.Height * baseScale;
            float offsetX = (_pictureBox.Width - itemWidth) / 2f;
            float offsetY = (_pictureBox.Height - itemHeight) / 2f;

            var state = g.Save();
            g.TranslateTransform(offsetX + _panOffset.X, offsetY + _panOffset.Y);
            g.ScaleTransform(baseScale * _zoomFactor, baseScale * _zoomFactor);

            g.DrawImage(_currentBitmap, 0, 0);

            if (_selections.Count > 0)
            {
                foreach (var sh in _selections) sh.DrawOn(g);
            }

            g.Restore(state);

            var col = GetRandomColor();

            switch (_tool)
            {
                case SelectionTool.Rectangle:
                    {
                        if (!_isDragging) break;
                        int x1 = Math.Min(_dragStartScr.X, _dragCurScr.X), y1 = Math.Min(_dragStartScr.Y, _dragCurScr.Y);
                        int w = Math.Abs(_dragCurScr.X - _dragStartScr.X), h = Math.Abs(_dragCurScr.Y - _dragStartScr.Y);
                        if (w > 1 && h > 1)
                        {
                            using var brush = new SolidBrush(Color.FromArgb(30, col));
                            using var pen = new Pen(col, 1.5f) { DashStyle = DashStyle.Dash };
                            g.FillRectangle(brush, x1, y1, w, h);
                            g.DrawRectangle(pen, x1, y1, w, h);
                        }
                        break;
                    }
                case SelectionTool.Circle:
                    {
                        if (!_isDragging) break;
                        float r = (float)Math.Sqrt(Math.Pow(_dragCurScr.X - _dragStartScr.X, 2) + Math.Pow(_dragCurScr.Y - _dragStartScr.Y, 2));
                        if (r > 1)
                        {
                            using var pen = new Pen(col, 1.5f) { DashStyle = DashStyle.Dash };
                            g.DrawEllipse(pen, _dragStartScr.X - r, _dragStartScr.Y - r, r * 2, r * 2);
                        }
                        break;
                    }
                case SelectionTool.Polygon:
                    {
                        if (!_polyActive || _polyScr.Count == 0) break;
                        var all = _polyScr.Concat(new[] { _polyMouse }).Select(p => (PointF)p).ToArray();
                        if (all.Length >= 3)
                        {
                            using var brush = new SolidBrush(Color.FromArgb(22, col));
                            g.FillPolygon(brush, all);
                        }
                        using var polyPen = new Pen(col, 1.5f) { DashStyle = DashStyle.Dash };
                        g.DrawLines(polyPen, all);
                        break;
                    }
                case SelectionTool.Freehand:
                    {
                        if (!_isDragging || _freeScr.Count < 2) break;
                        using var freePen = new Pen(col, 1.5f);
                        g.DrawLines(freePen, _freeScr.Select(p => (PointF)p).ToArray());
                        break;
                    }
            }
        }

        private void Pic_Down(object? s, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Middle)
            {
                _isPanning = true;
                _lastMousePos = e.Location;
                return;
            }

            var currentCube = _cube;
            if (currentCube == null || e.Button != MouseButtons.Left) return;
            var pt = MapToImage(e.Location); if (pt == null) return;
            switch (_tool)
            {
                case SelectionTool.Rectangle: case SelectionTool.Circle: _isDragging = true; _dragStartScr = e.Location; _dragStartImg = pt.Value; _dragCurScr = e.Location; break;
                case SelectionTool.Freehand: _isDragging = true; _freeImg.Clear(); _freeScr.Clear(); _freeImg.Add(pt.Value); _freeScr.Add(e.Location); break;
                case SelectionTool.Polygon: if (!_polyActive) { _polyActive = true; _polyImg.Clear(); _polyScr.Clear(); } _polyImg.Add(pt.Value); _polyScr.Add(e.Location); _polyMouse = e.Location; _pictureBox.Invalidate(); break;
            }
        }
        // --- FUNCIONES AUXILIARES FALTANTES ---

        private double WlAt(int b) => _cube != null && _cube.Header.Wavelengths != null && _cube.Header.Wavelengths.Count > b ? _cube.Header.Wavelengths[b] : b;

        private void Pic_DblClick(object? s, MouseEventArgs e)
        {
            if (_tool == SelectionTool.Polygon && _polyActive)
            {
                if (_polyImg.Count > 0)
                {
                    _polyImg.RemoveAt(_polyImg.Count - 1);
                    _polyScr.RemoveAt(_polyScr.Count - 1);
                }
                CommitPolygon();
            }
            else ClearAll();
        }

        private void Pic_Leave(object? s, EventArgs e)
        {
            _hoverImgPt = null;
            _lblCoords.Text = "";
            RedrawSpectrumPlot();
        }

        private void OpenChildForm(DockContent f)
        {
            f.FormClosed += (s, e) => _childForms.Remove(f);
            _childForms.Add(f);
            f.Show(_dockPanel, DockState.Document);
        }

        private void HandleDragEnter(object? sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
                if (files.Any(f => f.EndsWith(".hdr", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".raw", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".bil", StringComparison.OrdinalIgnoreCase)))
                    e.Effect = DragDropEffects.Copy;
            }
        }

        private void HandleDragDrop(object? sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
                var file = files.FirstOrDefault(f => f.EndsWith(".hdr", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".raw", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".bil", StringComparison.OrdinalIgnoreCase));
                if (file != null) LoadCubeFromFile(file);
            }
        }
        private void Pic_Move(object? s, MouseEventArgs e)
        {
            if (_isPanning)
            {
                _panOffset.X += (e.X - _lastMousePos.X);
                _panOffset.Y += (e.Y - _lastMousePos.Y);
                _lastMousePos = e.Location;
                _pictureBox.Invalidate();
                return;
            }

            var currentCube = _cube;
            if (currentCube == null) return;
            var pt = MapToImage(e.Location); _hoverImgPt = pt;

            if (pt != null)
            {
                int x = pt.Value.X, y = pt.Value.Y;
                if (x >= 0 && x < currentCube.Samples && y >= 0 && y < currentCube.Lines)
                {
                    float v = currentCube[_currentBand, y, x]; string bandStr = _cmbBands.Items.Count > _currentBand ? _cmbBands.Items[_currentBand].ToString()! : "N/A";
                    _lblCoords.Text = $"  X:{x}  Y:{y}  │  {bandStr}  │  val={v:G5}";
                    if (_graphicalInfoForm != null && !_graphicalInfoForm.IsDisposed) _graphicalInfoForm.UpdateData(_currentBand, new Point(x, y));
                }
                else _lblCoords.Text = "";
            }

            switch (_tool)
            {
                case SelectionTool.Rectangle: case SelectionTool.Circle: if (_isDragging) { _dragCurScr = e.Location; _pictureBox.Invalidate(); } break;
                case SelectionTool.Polygon: _polyMouse = e.Location; if (_polyActive) _pictureBox.Invalidate(); break;
                case SelectionTool.Freehand: if (_isDragging && pt != null) { var last = _freeScr.Count > 0 ? _freeScr[^1] : e.Location; if (Math.Abs(e.X - last.X) + Math.Abs(e.Y - last.Y) > 2) { _freeImg.Add(pt.Value); _freeScr.Add(e.Location); _pictureBox.Invalidate(); } } break;
            }
            RedrawSpectrumPlot();
        }

        private void Pic_Up(object? s, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Middle)
            {
                _isPanning = false;
                return;
            }

            var currentCube = _cube;
            if (currentCube == null) return;
            var pt = MapToImage(e.Location);

            if (e.Button == MouseButtons.Right && pt != null)
            {
                for (int i = _selections.Count - 1; i >= 0; i--)
                {
                    if (_selections[i].Contains(pt.Value))
                    {
                        SaveStateForUndo();
                        using var dlg = new MetadataDialog(_selections[i]);
                        if (dlg.ShowDialog() == DialogResult.OK) { RefreshDisplay(); _pictureBox.Invalidate(); }
                        else _undoStack.Pop();
                        return;
                    }
                }
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            switch (_tool)
            {
                case SelectionTool.Rectangle: { if (!_isDragging) break; _isDragging = false; _pictureBox.Invalidate(); if (pt == null) break; int dx = Math.Abs(pt.Value.X - _dragStartImg.X), dy = Math.Abs(pt.Value.Y - _dragStartImg.Y); Color col = GetRandomColor(); if (dx < 4 && dy < 4) AddShape(Activator.CreateInstance(Type.GetType("SpecimenFX17.Imaging.PixelShape")!, new object[] { new Point(_dragStartImg.X, _dragStartImg.Y), col }) as SelectionShape); else { int x1 = Clamp(Math.Min(_dragStartImg.X, pt.Value.X), 0, currentCube.Samples - 1), y1 = Clamp(Math.Min(_dragStartImg.Y, pt.Value.Y), 0, currentCube.Lines - 1); int x2 = Clamp(Math.Max(_dragStartImg.X, pt.Value.X), 0, currentCube.Samples - 1), y2 = Clamp(Math.Max(_dragStartImg.Y, pt.Value.Y), 0, currentCube.Lines - 1); AddShape(Activator.CreateInstance(Type.GetType("SpecimenFX17.Imaging.RectShape")!, new object[] { new Rectangle(x1, y1, x2 - x1, y2 - y1), col }) as SelectionShape); } break; }
                case SelectionTool.Circle: { if (!_isDragging) break; _isDragging = false; _pictureBox.Invalidate(); if (pt == null) break; int r = (int)Math.Round(Math.Sqrt(Math.Pow(pt.Value.X - _dragStartImg.X, 2) + Math.Pow(pt.Value.Y - _dragStartImg.Y, 2))); if (r > 1) AddShape(Activator.CreateInstance(Type.GetType("SpecimenFX17.Imaging.CircleShape")!, new object[] { _dragStartImg, r, GetRandomColor() }) as SelectionShape); break; }
                case SelectionTool.Freehand: { if (!_isDragging) break; _isDragging = false; _pictureBox.Invalidate(); if (_freeImg.Count >= 3) AddShape(Activator.CreateInstance(Type.GetType("SpecimenFX17.Imaging.FreehandShape")!, new object[] { _freeImg, GetRandomColor() }) as SelectionShape); _freeImg.Clear(); _freeScr.Clear(); break; }
                case SelectionTool.AutoDetect: { if (pt != null) { bool addMode = (Control.ModifierKeys & Keys.Shift) == Keys.Shift; bool subMode = (Control.ModifierKeys & Keys.Alt) == Keys.Alt; RunAutoRoi(pt.Value.X, pt.Value.Y, addMode, subMode); } break; }
                case SelectionTool.Point: { if (pt != null) AddShape(Activator.CreateInstance(Type.GetType("SpecimenFX17.Imaging.PixelShape")!, new object[] { new Point(pt.Value.X, pt.Value.Y), GetRandomColor() }) as SelectionShape); break; }
            }
        }

        private void AddShape(SelectionShape? sh)
        {
            if (sh == null) return;
            SaveStateForUndo();
            sh.Color = GetRandomColor();
            _selections.Add(sh);
            _btnClear.Enabled = true;
            RefreshDisplay();
            _pictureBox.Invalidate();
        }

        private void CommitPolygon() { if (_polyImg.Count >= 3) AddShape(Activator.CreateInstance(Type.GetType("SpecimenFX17.Imaging.PolygonShape")!, new object[] { _polyImg, GetRandomColor() }) as SelectionShape); _polyActive = false; _polyImg.Clear(); _polyScr.Clear(); _pictureBox.Invalidate(); }

        private void ClearAll()
        {
            if (_selections.Count == 0 && !_polyActive && _freeImg.Count == 0) return;
            SaveStateForUndo();
            _selections.Clear(); _polyActive = false; _polyImg.Clear(); _polyScr.Clear(); _freeImg.Clear(); _freeScr.Clear(); _btnClear.Enabled = false; ClearSpectrumPlot(); RefreshDisplay(); _pictureBox.Invalidate();
        }

        private void RunAutoRoi(int startX, int startY, bool addMode = false, bool subMode = false)
        {
            var currentCube = _cube;
            if (currentCube == null) return;
            dynamic? targetMask = null;
            if ((addMode || subMode) && _selections.Count > 0 && _selections.Last().GetType().Name == "MaskShape") targetMask = _selections.Last();
            _slbl.Text = "🪄 Analizando firma espectral (SAM)..."; _pb.Visible = true; _pb.Style = ProgressBarStyle.Marquee; _pictureBox.Enabled = false;
            float tolPercent = (float)_nudAutoTol.Value / 100f, maxAngleRads = tolPercent * 1.5f, minCos = (float)Math.Cos(maxAngleRads);
            Color col = targetMask != null ? targetMask.Color : GetRandomColor(); bool[,] mask = null!;
            int w = currentCube.Samples, h = currentCube.Lines;

            try
            {
                Task.Run(() => {
                    mask = new bool[h, w];
                    int numBands = 16, step = Math.Max(1, currentCube.Bands / numBands);
                    var bandsToUse = new List<int>(); for (int b = 0; b < currentCube.Bands; b += step) bandsToUse.Add(b);
                    float[] refSpec = new float[bandsToUse.Count]; float normRef = 0f;
                    for (int i = 0; i < bandsToUse.Count; i++) { float val = currentCube[bandsToUse[i], startY, startX]; refSpec[i] = float.IsNaN(val) ? 0 : val; normRef += refSpec[i] * refSpec[i]; }
                    normRef = (float)Math.Sqrt(normRef); if (normRef < 1e-6f) return;

                    var queue = new Queue<(int x, int y)>(w * h / 4); queue.Enqueue((startX, startY)); mask[startY, startX] = true;

                    int[] dx = { -1, 1, 0, 0 }, dy = { 0, 0, -1, 1 };
                    while (queue.Count > 0)
                    {
                        var (cx, cy) = queue.Dequeue();
                        for (int i = 0; i < 4; i++)
                        {
                            int nx = cx + dx[i], ny = cy + dy[i];
                            if (nx >= 0 && nx < w && ny >= 0 && ny < h && !mask[ny, nx])
                            {
                                float dot = 0f, normB = 0f;
                                for (int b = 0; b < bandsToUse.Count; b++)
                                {
                                    float val = currentCube[bandsToUse[b], ny, nx];
                                    if (float.IsNaN(val)) { normB = 0; break; }
                                    dot += refSpec[b] * val; normB += val * val;
                                }
                                if (normB >= 1e-6f && (dot / (normRef * (float)Math.Sqrt(normB))) >= minCos)
                                {
                                    mask[ny, nx] = true; queue.Enqueue((nx, ny));
                                }
                            }
                        }
                    }
                }).ContinueWith(t =>
                {
                    Invoke(() => {
                        if (mask != null)
                        {
                            if (targetMask != null)
                            {
                                SaveStateForUndo();
                                var oldMask = targetMask.GetMask(h, w);
                                var newMask = (bool[,])oldMask.Clone();

                                for (int y = 0; y < h; y++)
                                    for (int x = 0; x < w; x++)
                                    {
                                        if (mask[y, x])
                                        {
                                            if (addMode) newMask[y, x] = true;
                                            else if (subMode) newMask[y, x] = false;
                                        }
                                    }

                                var newShape = Activator.CreateInstance(Type.GetType("SpecimenFX17.Imaging.MaskShape")!, new object[] { newMask, targetMask.Color }) as SelectionShape;
                                newShape.Variety = targetMask.Variety;
                                newShape.Date = targetMask.Date;
                                newShape.MeasuredBrix = targetMask.MeasuredBrix;
                                newShape.Notes = targetMask.Notes;

                                int idx = _selections.IndexOf(targetMask);
                                if (idx >= 0) _selections[idx] = newShape;

                                RefreshDisplay();
                                _pictureBox.Invalidate();
                            }
                            else AddShape(Activator.CreateInstance(Type.GetType("SpecimenFX17.Imaging.MaskShape")!, new object[] { mask, col }) as SelectionShape);
                        }
                        _pictureBox.Enabled = true; _pb.Visible = false; _pb.Style = ProgressBarStyle.Continuous; _slbl.Text = "✔ Auto ROI completado";
                    });
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ocurrió un error en el algoritmo Auto-ROI:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _pictureBox.Enabled = true; _pb.Visible = false; _pb.Style = ProgressBarStyle.Continuous; _slbl.Text = "Error Auto ROI";
            }
        }
    }
}