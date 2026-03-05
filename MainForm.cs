using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SpecimenFX17.Imaging
{
    public class MainForm : Form
    {
        // ── Datos ─────────────────────────────────────────────────────────────
        private HyperspectralCube? _cube;
        private int  _currentBand   = 0;
        private bool _grayscaleMode = false;
        private Bitmap? _currentBitmap;

        // Puntos y rectángulos seleccionados
        private readonly List<(Point Img, Color Color)> _selectedPixels = new();
        private readonly List<(Rectangle ImgRect, Color Color)> _selectedRects = new();
        private static readonly Color[] PixelColors =
        {
            Color.Cyan, Color.Yellow, Color.LimeGreen,
            Color.OrangeRed, Color.Magenta, Color.White
        };

        // Estado del arrastre para dibujar rectángulo
        private bool  _isDragging    = false;
        private Point _dragStartScreen;   // coordenada de pantalla donde empezó el drag
        private Point _dragStartImg;      // coordenada de imagen donde empezó el drag
        private Point _dragCurrentScreen; // posición actual durante el drag

        // ── Controles ─────────────────────────────────────────────────────────
        private PictureBox    _pictureBox      = null!;
        private PictureBox    _spectrumPlot    = null!;
        private Label         _lblCoords       = null!;
        private Label         _lblWavelength   = null!;
        private Label         _lblSpectrumInfo = null!;
        private Label         _lblBandInfo     = null!;
        private TrackBar      _bandSlider      = null!;
        private ComboBox      _cmbColormap     = null!;
        private CheckBox      _chkColorbar     = null!;
        private CheckBox      _chkGrayscale    = null!;
        private NumericUpDown _nudGamma        = null!;
        private NumericUpDown _nudLowPct       = null!;
        private NumericUpDown _nudHighPct      = null!;
        private NumericUpDown _nudThreshold    = null!;
        private Button        _btnLoad         = null!;
        private Button        _btnExport       = null!;
        private Button        _btnExportAll    = null!;
        private Button        _btnClearPixels  = null!;
        private ProgressBar   _progressBar     = null!;
        private StatusStrip   _statusStrip     = null!;
        private ToolStripStatusLabel _statusLabel = null!;

        // ─────────────────────────────────────────────────────────────────────

        public MainForm()
        {
            Text        = "SpecimenFX17 — Visor BLI Hiperespectral";
            Size        = new Size(1400, 900);
            MinimumSize = new Size(1000, 650);
            BackColor   = Color.FromArgb(18, 18, 26);
            ForeColor   = Color.White;
            Font        = new Font("Segoe UI", 9f);
            BuildUI();
        }

        // ═════════════════════════════════════════════════════════════════════
        //  CONSTRUCCIÓN DE LA INTERFAZ  (sin SplitContainer)
        // ═════════════════════════════════════════════════════════════════════

        private void BuildUI()
        {
            // ── Status bar (Dock Bottom) ──────────────────────────────────────
            _statusStrip = new StatusStrip
            {
                BackColor = Color.FromArgb(12, 12, 20), SizingGrip = false
            };
            _statusLabel = new ToolStripStatusLabel("Carga un archivo .hdr para comenzar")
            {
                ForeColor = Color.FromArgb(100, 200, 100), Spring = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            _progressBar = new ProgressBar
            {
                Style = ProgressBarStyle.Continuous, Visible = false, Size = new Size(200, 14)
            };
            _statusStrip.Items.Add(_statusLabel);
            _statusStrip.Items.Add(new ToolStripControlHost(_progressBar));

            // ── Panel derecho de controles (Dock Right, ancho fijo) ───────────
            var rightPanel = new Panel
            {
                Dock = DockStyle.Right, Width = 240,
                BackColor = Color.FromArgb(24, 24, 36),
                AutoScroll = true, Padding = new Padding(10, 8, 8, 6)
            };
            BuildRightPanel(rightPanel);

            // ── Panel central (Fill): imagen arriba, gráfico abajo ────────────
            var centerPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };

            // -- Slider de banda (Dock Top dentro del centro) ------------------
            var sliderPanel = new Panel
            {
                Dock = DockStyle.Top, Height = 46,
                BackColor = Color.FromArgb(22, 22, 34)
            };

            _lblWavelength = new Label
            {
                Dock = DockStyle.Bottom, Height = 18,
                ForeColor = Color.FromArgb(100, 210, 255),
                Font = new Font("Consolas", 8.5f, FontStyle.Bold),
                Text = "  Carga un archivo .hdr",
                TextAlign = ContentAlignment.MiddleLeft
            };
            _bandSlider = new TrackBar
            {
                Dock = DockStyle.Fill, Minimum = 0, Maximum = 0,
                TickStyle = TickStyle.None,
                BackColor = Color.FromArgb(22, 22, 34)
            };
            _bandSlider.Scroll += (_, _) => { _currentBand = _bandSlider.Value; RefreshDisplay(); };
            sliderPanel.Controls.Add(_bandSlider);
            sliderPanel.Controls.Add(_lblWavelength);

            // -- Panel del gráfico espectral (Dock Bottom) ---------------------
            var spectrumContainer = new Panel
            {
                Dock = DockStyle.Bottom, Height = 200,
                BackColor = Color.FromArgb(12, 12, 20)
            };

            _lblSpectrumInfo = new Label
            {
                Dock = DockStyle.Top, Height = 20,
                BackColor = Color.FromArgb(20, 20, 32),
                ForeColor = Color.FromArgb(140, 140, 190),
                Font = new Font("Segoe UI", 8f, FontStyle.Italic),
                Text = "  Haz clic en un píxel para ver su espectro  •  Clic adicional = nueva curva  •  Doble clic = limpiar",
                TextAlign = ContentAlignment.MiddleLeft
            };
            _spectrumPlot = new PictureBox
            {
                Dock = DockStyle.Fill, BackColor = Color.FromArgb(12, 12, 20),
                SizeMode = PictureBoxSizeMode.Normal
            };
            _spectrumPlot.Resize += (_, _) => RedrawSpectrumPlot();

            spectrumContainer.Controls.Add(_spectrumPlot);
            spectrumContainer.Controls.Add(_lblSpectrumInfo);

            // Divisor visual entre imagen y gráfico
            var divider = new Panel
            {
                Dock = DockStyle.Bottom, Height = 3,
                BackColor = Color.FromArgb(50, 50, 70)
            };

            // -- PictureBox de la imagen (Fill, lo que queda) ------------------
            _pictureBox = new PictureBox
            {
                Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black, Cursor = Cursors.Cross
            };
            _pictureBox.MouseMove        += PictureBox_MouseMove;
            _pictureBox.MouseDown        += PictureBox_MouseDown;
            _pictureBox.MouseUp          += PictureBox_MouseUp;
            _pictureBox.Paint            += PictureBox_Paint;
            _pictureBox.MouseDoubleClick += (_, _) =>
            {
                _selectedPixels.Clear();
                _selectedRects.Clear();
                ClearSpectrumPlot();
            };

            // Etiqueta de coordenadas flotante sobre la imagen
            _lblCoords = new Label
            {
                AutoSize = false, Size = new Size(320, 20), Location = new Point(6, 6),
                BackColor = Color.FromArgb(160, 0, 0, 0),
                ForeColor = Color.FromArgb(200, 255, 200),
                Font = new Font("Consolas", 8f), Text = "",
                TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(4, 0, 0, 0)
            };
            _pictureBox.Controls.Add(_lblCoords);

            // Ensamblar el panel central (el orden de Add importa con Dock)
            centerPanel.Controls.Add(_pictureBox);       // Fill → ocupa lo que queda
            centerPanel.Controls.Add(divider);           // Bottom
            centerPanel.Controls.Add(spectrumContainer); // Bottom
            centerPanel.Controls.Add(sliderPanel);       // Top

            // Ensamblar el formulario
            Controls.Add(centerPanel);   // Fill
            Controls.Add(rightPanel);    // Right  (añadir antes que Fill)
            Controls.Add(_statusStrip);  // Bottom
        }

        private void BuildRightPanel(Panel p)
        {
            int cy = 8;

            _btnLoad = Btn(p, "📂  Cargar .hdr / .raw", ref cy, Color.FromArgb(40, 90, 140));
            _btnLoad.Click += BtnLoad_Click;

            Sep(p, ref cy);
            Sec(p, "VISUALIZACIÓN", ref cy);

            Lbl(p, "Paleta de color:", ref cy);
            _cmbColormap = new ComboBox
            {
                Location = new Point(8, cy), Width = 210,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(38, 38, 55), ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _cmbColormap.Items.AddRange(Enum.GetNames(typeof(BliColormap)));
            _cmbColormap.SelectedIndex = 0;
            _cmbColormap.SelectedIndexChanged += (_, _) => RefreshDisplay();
            p.Controls.Add(_cmbColormap); cy += 28;

            _chkColorbar  = Chk(p, "Mostrar barra de escala",  ref cy, true);
            _chkGrayscale = Chk(p, "Modo escala de grises",    ref cy, false);
            _chkColorbar.CheckedChanged  += (_, _) => RefreshDisplay();
            _chkGrayscale.CheckedChanged += (_, _) =>
            {
                _grayscaleMode = _chkGrayscale.Checked;
                _cmbColormap.Enabled = !_grayscaleMode;
                RefreshDisplay();
            };

            Sep(p, ref cy);
            Sec(p, "AJUSTES DE IMAGEN", ref cy);

            Lbl(p, "Gamma (1 = lineal):",  ref cy); _nudGamma     = Num(p, ref cy, 1.0m, 0.1m, 5.0m,    0.1m, 1);
            Lbl(p, "Percentil bajo (%):",  ref cy); _nudLowPct    = Num(p, ref cy, 2m,   0m,   49m,      1m,   0);
            Lbl(p, "Percentil alto (%):",  ref cy); _nudHighPct   = Num(p, ref cy, 98m,  51m,  100m,     1m,   0);
            Lbl(p, "Umbral de señal:",     ref cy); _nudThreshold = Num(p, ref cy, 0m,   0m,   9999999m, 1m,   0);

            foreach (var n in new[] { _nudGamma, _nudLowPct, _nudHighPct, _nudThreshold })
                n.ValueChanged += (_, _) => RefreshDisplay();

            Sep(p, ref cy);
            Sec(p, "EXPORTAR", ref cy);

            _btnExport      = Btn(p, "💾  Exportar banda actual",      ref cy, Color.FromArgb(35, 95, 55));
            _btnExportAll   = Btn(p, "📦  Exportar todas las bandas",  ref cy, Color.FromArgb(30, 75, 45));
            _btnClearPixels = Btn(p, "🗑️  Limpiar puntos del gráfico", ref cy, Color.FromArgb(110, 40, 40));

            _btnExport.Enabled = _btnExportAll.Enabled = _btnClearPixels.Enabled = false;
            _btnExport.Click      += BtnExport_Click;
            _btnExportAll.Click   += BtnExportAll_Click;
            _btnClearPixels.Click += (_, _) => { _selectedPixels.Clear(); _selectedRects.Clear(); ClearSpectrumPlot(); };

            Sep(p, ref cy);
            Sec(p, "CALCULADORA ESPECTRAL", ref cy);
            var btnCalc = Btn(p, "🧮  Abrir calculadora", ref cy, Color.FromArgb(70, 45, 110));
            btnCalc.Click += (_, _) =>
            {
                if (_cube == null) { MessageBox.Show("Carga un cubo primero.", "Aviso"); return; }
                new SpectralCalculatorForm(_cube).Show();
            };

            Sep(p, ref cy);
            Sec(p, "INFO DE BANDA", ref cy);

            _lblBandInfo = new Label
            {
                Location = new Point(8, cy), Width = 210, Height = 100,
                ForeColor = Color.FromArgb(160, 160, 190),
                Font = new Font("Consolas", 7.5f), Text = "—", AutoSize = false
            };
            p.Controls.Add(_lblBandInfo);
        }

        // ── Helpers de controles ──────────────────────────────────────────────
        private Button Btn(Panel p, string text, ref int cy, Color bg)
        {
            var b = new Button
            {
                Text = text, Location = new Point(8, cy), Width = 210, Height = 30,
                FlatStyle = FlatStyle.Flat, BackColor = bg, ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(
                Math.Min(255, bg.R + 35), Math.Min(255, bg.G + 35), Math.Min(255, bg.B + 35));
            p.Controls.Add(b); cy += 36; return b;
        }

        private NumericUpDown Num(Panel p, ref int cy,
            decimal val, decimal min, decimal max, decimal inc, int dec)
        {
            var n = new NumericUpDown
            {
                Location = new Point(8, cy), Width = 210,
                Minimum = min, Maximum = max, Value = val, Increment = inc, DecimalPlaces = dec,
                BackColor = Color.FromArgb(36, 36, 52), ForeColor = Color.White
            };
            p.Controls.Add(n); cy += 26; return n;
        }

        private void Lbl(Panel p, string t, ref int cy)
        {
            p.Controls.Add(new Label
            {
                Text = t, Location = new Point(8, cy), Width = 210, Height = 16,
                ForeColor = Color.FromArgb(140, 140, 170), Font = new Font("Segoe UI", 8f)
            });
            cy += 17;
        }

        private void Sec(Panel p, string t, ref int cy)
        {
            p.Controls.Add(new Label
            {
                Text = t, Location = new Point(8, cy), Width = 210, Height = 18,
                ForeColor = Color.FromArgb(100, 160, 220),
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            });
            cy += 20;
        }

        private CheckBox Chk(Panel p, string t, ref int cy, bool chk)
        {
            var c = new CheckBox
            {
                Text = t, Location = new Point(8, cy), Width = 210, Checked = chk,
                ForeColor = Color.FromArgb(180, 180, 210), BackColor = Color.Transparent
            };
            p.Controls.Add(c); cy += 24; return c;
        }

        private void Sep(Panel p, ref int cy)
        {
            p.Controls.Add(new Label
            {
                Location = new Point(8, cy), Width = 210, Height = 1,
                BackColor = Color.FromArgb(55, 55, 75)
            });
            cy += 10;
        }

        // ═════════════════════════════════════════════════════════════════════
        //  EVENTOS DE LA IMAGEN
        // ═════════════════════════════════════════════════════════════════════

        // ── DRAG: inicio ─────────────────────────────────────────────────────
        private void PictureBox_MouseDown(object? s, MouseEventArgs e)
        {
            if (_cube == null || e.Button != MouseButtons.Left) return;
            var pt = MapToImage(e.Location);
            if (pt == null) return;
            _isDragging       = true;
            _dragStartScreen  = e.Location;
            _dragStartImg     = pt.Value;
            _dragCurrentScreen = e.Location;
        }

        // ── DRAG: movimiento → dibuja el rectángulo de selección en vivo ─────
        private void PictureBox_MouseMove(object? s, MouseEventArgs e)
        {
            if (_cube == null) return;

            var pt = MapToImage(e.Location);
            if (pt != null)
            {
                int x = pt.Value.X, y = pt.Value.Y;
                if (x >= 0 && x < _cube.Samples && y >= 0 && y < _cube.Lines)
                {
                    float  val = _cube[_currentBand, y, x];
                    double wl  = WavelengthAt(_currentBand);
                    _lblCoords.Text   = $"  X: {x}   Y: {y}   │   λ = {wl:F1} nm   │   val = {val:G5}";
                    _statusLabel.Text = $"Píxel ({x}, {y})   banda {_currentBand + 1}/{_cube.Bands}   " +
                                        $"λ = {wl:F1} {_cube.Header.WavelengthUnits}   valor = {val:G6}";
                }
                else _lblCoords.Text = "";
            }

            if (_isDragging)
            {
                _dragCurrentScreen = e.Location;
                _pictureBox.Invalidate(); // provoca Paint para dibujar el rect en vivo
            }
        }

        // ── DRAG: fin → confirmar selección ──────────────────────────────────
        private void PictureBox_MouseUp(object? s, MouseEventArgs e)
        {
            if (_cube == null || e.Button != MouseButtons.Left) return;

            if (!_isDragging) return;
            _isDragging = false;
            _pictureBox.Invalidate();

            var endPt = MapToImage(e.Location);
            if (endPt == null) return;

            int dx = Math.Abs(endPt.Value.X - _dragStartImg.X);
            int dy = Math.Abs(endPt.Value.Y - _dragStartImg.Y);

            if (dx < 4 && dy < 4)
            {
                // Fue un clic simple → añadir punto
                int x = _dragStartImg.X, y = _dragStartImg.Y;
                if (x < 0 || x >= _cube.Samples || y < 0 || y >= _cube.Lines) return;

                int totalSel = _selectedPixels.Count + _selectedRects.Count;
                if (totalSel >= PixelColors.Length)
                {
                    if (_selectedPixels.Count > 0) _selectedPixels.RemoveAt(0);
                    else _selectedRects.RemoveAt(0);
                }
                int colorIdx = (_selectedPixels.Count + _selectedRects.Count) % PixelColors.Length;
                _selectedPixels.Add((new Point(x, y), PixelColors[colorIdx]));
            }
            else
            {
                // Fue un arrastre → añadir rectángulo
                int x1 = Math.Clamp(Math.Min(_dragStartImg.X, endPt.Value.X), 0, _cube.Samples - 1);
                int y1 = Math.Clamp(Math.Min(_dragStartImg.Y, endPt.Value.Y), 0, _cube.Lines   - 1);
                int x2 = Math.Clamp(Math.Max(_dragStartImg.X, endPt.Value.X), 0, _cube.Samples - 1);
                int y2 = Math.Clamp(Math.Max(_dragStartImg.Y, endPt.Value.Y), 0, _cube.Lines   - 1);

                int totalSel = _selectedPixels.Count + _selectedRects.Count;
                if (totalSel >= PixelColors.Length)
                {
                    if (_selectedRects.Count > 0) _selectedRects.RemoveAt(0);
                    else _selectedPixels.RemoveAt(0);
                }
                int colorIdx = (_selectedPixels.Count + _selectedRects.Count) % PixelColors.Length;
                _selectedRects.Add((new Rectangle(x1, y1, x2 - x1, y2 - y1), PixelColors[colorIdx]));
            }

            _btnClearPixels.Enabled = true;
            DrawPixelMarkers();
            RedrawSpectrumPlot();
        }

        // ── PAINT: dibuja el rectángulo de arrastre en vivo ───────────────────
        private void PictureBox_Paint(object? s, PaintEventArgs e)
        {
            if (!_isDragging) return;

            int x1 = Math.Min(_dragStartScreen.X, _dragCurrentScreen.X);
            int y1 = Math.Min(_dragStartScreen.Y, _dragCurrentScreen.Y);
            int w  = Math.Abs(_dragCurrentScreen.X - _dragStartScreen.X);
            int h  = Math.Abs(_dragCurrentScreen.Y - _dragStartScreen.Y);
            if (w < 2 || h < 2) return;

            int colorIdx = (_selectedPixels.Count + _selectedRects.Count) % PixelColors.Length;
            var col = PixelColors[colorIdx];

            // Relleno semitransparente
            using var fill = new SolidBrush(Color.FromArgb(35, col.R, col.G, col.B));
            e.Graphics.FillRectangle(fill, x1, y1, w, h);

            // Borde discontinuo
            using var pen = new Pen(col, 1.5f) { DashStyle = DashStyle.Dash };
            e.Graphics.DrawRectangle(pen, x1, y1, w, h);

            // Esquinas sólidas para mejor visibilidad
            int c = 6;
            using var cornerPen = new Pen(col, 2f);
            e.Graphics.DrawLine(cornerPen, x1,     y1,     x1 + c, y1);
            e.Graphics.DrawLine(cornerPen, x1,     y1,     x1,     y1 + c);
            e.Graphics.DrawLine(cornerPen, x1 + w, y1,     x1 + w - c, y1);
            e.Graphics.DrawLine(cornerPen, x1 + w, y1,     x1 + w,     y1 + c);
            e.Graphics.DrawLine(cornerPen, x1,     y1 + h, x1 + c,     y1 + h);
            e.Graphics.DrawLine(cornerPen, x1,     y1 + h, x1,         y1 + h - c);
            e.Graphics.DrawLine(cornerPen, x1 + w, y1 + h, x1 + w - c, y1 + h);
            e.Graphics.DrawLine(cornerPen, x1 + w, y1 + h, x1 + w,     y1 + h - c);

            // Tamaño del área en píxeles de imagen
            var startImg = MapToImage(_dragStartScreen);
            var curImg   = MapToImage(_dragCurrentScreen);
            if (startImg != null && curImg != null)
            {
                int pw = Math.Abs(curImg.Value.X - startImg.Value.X);
                int ph = Math.Abs(curImg.Value.Y - startImg.Value.Y);
                string info = $"{pw} × {ph} px";
                using var font  = new Font("Consolas", 8f);
                using var bg    = new SolidBrush(Color.FromArgb(150, 0, 0, 0));
                using var brush = new SolidBrush(col);
                var sz = e.Graphics.MeasureString(info, font);
                e.Graphics.FillRectangle(bg, x1 + w / 2 - sz.Width / 2 - 2, y1 + h / 2 - sz.Height / 2 - 1,
                                         sz.Width + 4, sz.Height + 2);
                e.Graphics.DrawString(info, font, brush, x1 + w / 2 - sz.Width / 2, y1 + h / 2 - sz.Height / 2);
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  CARGA DEL CUBO
        // ═════════════════════════════════════════════════════════════════════

        private async void BtnLoad_Click(object? s, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Title  = "Abrir imagen hiperespectral ENVI",
                Filter = "ENVI Header (*.hdr)|*.hdr|Datos RAW (*.raw)|*.raw|Todos|*.*"
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            await LoadCubeAsync(dlg.FileName);
        }

        private async Task LoadCubeAsync(string path)
        {
            _btnLoad.Enabled     = false;
            _progressBar.Visible = true;
            _progressBar.Value   = 0;
            _statusLabel.Text    = "Cargando cubo hiperespectral…";

            var prog = new Progress<int>(v =>
            {
                _progressBar.Value = v;
                _statusLabel.Text  = $"Cargando… {v} %";
            });

            try
            {
                _cube = await Task.Run(() => HyperspectralCube.Load(path, prog));

                _selectedPixels.Clear();
                _bandSlider.Minimum = 0;
                _bandSlider.Maximum = Math.Max(0, _cube.Bands - 1);
                _bandSlider.Value   = 0;
                _currentBand        = 0;

                _btnExport.Enabled = _btnExportAll.Enabled = true;
                RefreshDisplay();
                ClearSpectrumPlot();
                _statusLabel.Text = $"✔  {_cube.Header}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar el archivo:\n\n{ex.Message}",
                    "Error de carga", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _statusLabel.Text = "Error de carga";
            }
            finally
            {
                _progressBar.Visible = false;
                _btnLoad.Enabled     = true;
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  RENDERIZADO DE IMAGEN
        // ═════════════════════════════════════════════════════════════════════

        private void RefreshDisplay()
        {
            if (_cube == null) return;
            RenderCleanBitmap();                              // bitmap limpio
            bool hasSelection = _selectedPixels.Count > 0 || _selectedRects.Count > 0;
            if (hasSelection) { DrawPixelMarkers(); RedrawSpectrumPlot(); }
        }

        private void UpdateBandLabels()
        {
            if (_cube == null) return;
            double wl = WavelengthAt(_currentBand);
            _lblWavelength.Text = $"  Banda {_currentBand + 1} / {_cube.Bands}   │   " +
                                  $"λ = {wl:F2} {_cube.Header.WavelengthUnits}   │   " +
                                  $"{_cube.Samples} × {_cube.Lines} px";
            var (bMin, bMax) = _cube.GetBandStats(_currentBand);
            _lblBandInfo.Text = $"Banda:  {_currentBand + 1}\n" +
                                $"λ:      {wl:F2} {_cube.Header.WavelengthUnits}\n" +
                                $"Mín:    {bMin:G5}\n" +
                                $"Máx:    {bMax:G5}\n" +
                                $"Ancho:  {_cube.Samples} px\n" +
                                $"Alto:   {_cube.Lines} px";
        }

        private void DrawPixelMarkers()
        {
            if (_currentBitmap == null || _cube == null) return;

            using var g = Graphics.FromImage(_currentBitmap);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // ── Puntos (clic simple) ──────────────────────────────────────────
            foreach (var (pt, color) in _selectedPixels)
            {
                int r = 7;
                using var bgPen = new Pen(Color.FromArgb(180, 0, 0, 0), 3.5f);
                g.DrawLine(bgPen, pt.X - r, pt.Y, pt.X + r, pt.Y);
                g.DrawLine(bgPen, pt.X, pt.Y - r, pt.X, pt.Y + r);
                g.DrawEllipse(bgPen, pt.X - r / 2, pt.Y - r / 2, r, r);

                using var pen = new Pen(color, 1.8f);
                g.DrawLine(pen, pt.X - r, pt.Y, pt.X + r, pt.Y);
                g.DrawLine(pen, pt.X, pt.Y - r, pt.X, pt.Y + r);
                g.DrawEllipse(pen, pt.X - r / 2, pt.Y - r / 2, r, r);

                using var font  = new Font("Consolas", 7f, FontStyle.Bold);
                using var brush = new SolidBrush(color);
                g.DrawString($"({pt.X},{pt.Y})", font, brush, pt.X + r + 2, pt.Y - 7);
            }

            // ── Rectángulos (arrastre) ────────────────────────────────────────
            foreach (var (rect, color) in _selectedRects)
            {
                // Relleno semitransparente
                using var fill = new SolidBrush(Color.FromArgb(30, color.R, color.G, color.B));
                g.FillRectangle(fill, rect);

                // Borde
                using var pen = new Pen(color, 1.8f);
                g.DrawRectangle(pen, rect);

                // Esquinas reforzadas
                int c = 8;
                using var cp = new Pen(color, 2.5f);
                g.DrawLine(cp, rect.Left,          rect.Top,           rect.Left + c, rect.Top);
                g.DrawLine(cp, rect.Left,          rect.Top,           rect.Left,     rect.Top + c);
                g.DrawLine(cp, rect.Right,         rect.Top,           rect.Right - c,rect.Top);
                g.DrawLine(cp, rect.Right,         rect.Top,           rect.Right,    rect.Top + c);
                g.DrawLine(cp, rect.Left,          rect.Bottom,        rect.Left + c, rect.Bottom);
                g.DrawLine(cp, rect.Left,          rect.Bottom,        rect.Left,     rect.Bottom - c);
                g.DrawLine(cp, rect.Right,         rect.Bottom,        rect.Right - c,rect.Bottom);
                g.DrawLine(cp, rect.Right,         rect.Bottom,        rect.Right,    rect.Bottom - c);

                // Etiqueta: tamaño y coordenada origen
                using var font  = new Font("Consolas", 7f, FontStyle.Bold);
                using var bg    = new SolidBrush(Color.FromArgb(160, 0, 0, 0));
                using var brush = new SolidBrush(color);
                string lbl = $"({rect.X},{rect.Y}) {rect.Width}×{rect.Height}";
                var sz = g.MeasureString(lbl, font);
                float lx = rect.Left + 2, ly = rect.Top - sz.Height - 1;
                if (ly < 0) ly = rect.Top + 2;
                g.FillRectangle(bg, lx - 1, ly - 1, sz.Width + 2, sz.Height + 2);
                g.DrawString(lbl, font, brush, lx, ly);
            }

            _pictureBox.Image = _currentBitmap;
        }

        // ═════════════════════════════════════════════════════════════════════
        //  GRÁFICO ESPECTRAL
        // ═════════════════════════════════════════════════════════════════════

        private void ClearSpectrumPlot()
        {
            _spectrumPlot.Image?.Dispose();
            _spectrumPlot.Image     = null;
            _btnClearPixels.Enabled = false;
            _lblSpectrumInfo.Text   =
                "  Clic = seleccionar píxel  •  Arrastrar = seleccionar área (espectro promedio)  •  Doble clic = limpiar";

            // Regenerar bitmap limpio SIN marcadores pintados encima
            if (_cube != null) RenderCleanBitmap();
        }

        /// <summary>Genera un bitmap nuevo sin marcadores y lo asigna al PictureBox.</summary>
        private void RenderCleanBitmap()
        {
            if (_cube == null) return;
            var opts = BuildOptions();
            if (_grayscaleMode) opts.Colormap = BliColormap.Grayscale;
            _currentBitmap?.Dispose();
            _currentBitmap    = BliRenderer.RenderBand(_cube, _currentBand, opts);
            _pictureBox.Image = _currentBitmap;
            UpdateBandLabels();
        }

        /// <summary>Calcula el espectro promedio de todos los píxeles dentro de un rectángulo.</summary>
        private float[] AverageSpectrum(Rectangle r)
        {
            var result = new float[_cube!.Bands];
            int count  = 0;
            for (int y = r.Top; y <= r.Bottom && y < _cube.Lines; y++)
                for (int x = r.Left; x <= r.Right && x < _cube.Samples; x++)
                {
                    for (int b = 0; b < _cube.Bands; b++)
                        result[b] += _cube[b, y, x];
                    count++;
                }
            if (count > 0)
                for (int b = 0; b < _cube.Bands; b++)
                    result[b] /= count;
            return result;
        }

        private void RedrawSpectrumPlot()
        {
            if (_cube == null || (_selectedPixels.Count == 0 && _selectedRects.Count == 0)) return;

            int w = Math.Max(_spectrumPlot.Width,  300);
            int h = Math.Max(_spectrumPlot.Height, 80);

            var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(Color.FromArgb(12, 12, 20));

            const int padL = 64, padR = 20, padT = 24, padB = 40;
            var plot = new Rectangle(padL, padT, w - padL - padR, h - padT - padB);
            if (plot.Width < 20 || plot.Height < 10) { _spectrumPlot.Image = bmp; return; }

            // Rango Y
            float yMin = float.MaxValue, yMax = float.MinValue;
            foreach (var (pt, _) in _selectedPixels)
            {
                var sp = _cube.GetSpectrum(pt.Y, pt.X);
                foreach (float v in sp) { if (v < yMin) yMin = v; if (v > yMax) yMax = v; }
            }
            foreach (var (rect, _) in _selectedRects)
            {
                var sp = AverageSpectrum(rect);
                foreach (float v in sp) { if (v < yMin) yMin = v; if (v > yMax) yMax = v; }
            }
            if (yMin == float.MaxValue) { yMin = 0; yMax = 1; }
            float yRange = yMax - yMin; if (yRange < 1e-10f) yRange = 1f;
            yMin -= yRange * 0.05f; yMax += yRange * 0.05f; yRange = yMax - yMin;

            // Rango X
            var wls    = _cube.Header.Wavelengths;
            double xMin = wls.Count > 0 ? wls[0]  : 0;
            double xMax = wls.Count > 0 ? wls[^1] : _cube.Bands - 1;
            double xRng = xMax - xMin; if (xRng < 1e-10) xRng = 1;

            // Grid
            using (var gp = new Pen(Color.FromArgb(28, 255, 255, 255), 1f) { DashStyle = DashStyle.Dot })
            {
                for (int i = 0; i <= 5; i++)
                    g.DrawLine(gp, plot.Left, plot.Bottom - (float)i / 5 * plot.Height,
                                   plot.Right, plot.Bottom - (float)i / 5 * plot.Height);
                for (int i = 0; i <= 6; i++)
                    g.DrawLine(gp, plot.Left + (float)i / 6 * plot.Width, plot.Top,
                                   plot.Left + (float)i / 6 * plot.Width, plot.Bottom);
            }
            using (var bp = new Pen(Color.FromArgb(65, 255, 255, 255)))
                g.DrawRectangle(bp, plot);

            // Línea de banda actual
            double curWl = WavelengthAt(_currentBand);
            float  curPx = Px(curWl, xMin, xRng, plot);
            using (var dp = new Pen(Color.FromArgb(110, 255, 255, 80), 1f) { DashStyle = DashStyle.Dash })
                g.DrawLine(dp, curPx, plot.Top, curPx, plot.Bottom);

            // ── Curvas de píxeles individuales ───────────────────────────────
            foreach (var (pt, color) in _selectedPixels)
            {
                var spec = _cube.GetSpectrum(pt.Y, pt.X);
                if (spec.Length < 2) continue;
                DrawCurve(g, spec, wls, plot, xMin, xRng, yMin, yRange, color);

                // Punto en banda actual
                float val = _cube[_currentBand, pt.Y, pt.X];
                float dpx = Px(curWl, xMin, xRng, plot);
                float dpy = Py(val, yMin, yRange, plot);
                using (var fb = new SolidBrush(color))    g.FillEllipse(fb, dpx - 5, dpy - 5, 10, 10);
                using (var ep = new Pen(Color.White, 1f)) g.DrawEllipse(ep, dpx - 5, dpy - 5, 10, 10);
            }

            // ── Curvas de rectángulos (espectro promedio) → línea discontinua ─
            foreach (var (rect, color) in _selectedRects)
            {
                var spec = AverageSpectrum(rect);
                if (spec.Length < 2) continue;
                DrawCurve(g, spec, wls, plot, xMin, xRng, yMin, yRange, color, dashed: true);

                // Punto en banda actual
                float val = spec[_currentBand];
                float dpx = Px(curWl, xMin, xRng, plot);
                float dpy = Py(val, yMin, yRange, plot);
                using (var fb = new SolidBrush(color))    g.FillRectangle(fb, dpx - 5, dpy - 5, 10, 10);
                using (var ep = new Pen(Color.White, 1f)) g.DrawRectangle(ep, dpx - 5, dpy - 5, 10, 10);
            }

            // Ejes
            using var tf = new Font("Consolas", 7.5f);
            using var tb = new SolidBrush(Color.FromArgb(160, 160, 195));
            using var af = new Font("Segoe UI", 8f, FontStyle.Italic);
            using var ab = new SolidBrush(Color.FromArgb(120, 130, 165));

            for (int i = 0; i <= 7; i++)
            {
                double wl = xMin + xRng * i / 7;
                float  px = plot.Left + (float)(i / 7.0 * plot.Width);
                string lb = wl >= 1000 ? $"{wl/1000:F1}µ" : $"{wl:F0}";
                var sz = g.MeasureString(lb, tf);
                g.DrawString(lb, tf, tb, px - sz.Width / 2, plot.Bottom + 2);
            }
            var xts = g.MeasureString($"Longitud de onda ({_cube.Header.WavelengthUnits})", af);
            g.DrawString($"Longitud de onda ({_cube.Header.WavelengthUnits})", af, ab,
                plot.Left + plot.Width / 2f - xts.Width / 2f, plot.Bottom + 20);

            for (int i = 0; i <= 5; i++)
            {
                float v  = yMin + yRange * i / 5;
                float py = plot.Bottom - (float)i / 5 * plot.Height;
                string lb = FmtVal(v);
                var sz = g.MeasureString(lb, tf);
                g.DrawString(lb, tf, tb, plot.Left - sz.Width - 3, py - sz.Height / 2);
            }
            var st = g.Save();
            g.TranslateTransform(10, plot.Top + plot.Height / 2f);
            g.RotateTransform(-90);
            g.DrawString("Intensidad / Reflectancia", af, ab, -72, -6);
            g.Restore(st);

            // Leyenda
            using var lf = new Font("Consolas", 7.5f);
            int lx = plot.Right - 120, ly = plot.Top + 4;
            foreach (var (pt, color) in _selectedPixels)
            {
                using var lpen   = new Pen(color, 2f);
                using var lbrush = new SolidBrush(color);
                g.DrawLine(lpen, lx, ly + 5, lx + 16, ly + 5);
                g.DrawString($"·({pt.X},{pt.Y})", lf, lbrush, lx + 20, ly);
                ly += 14;
            }
            foreach (var (rect, color) in _selectedRects)
            {
                using var lpen   = new Pen(color, 2f) { DashStyle = DashStyle.Dash };
                using var lbrush = new SolidBrush(color);
                g.DrawLine(lpen, lx, ly + 5, lx + 16, ly + 5);
                g.DrawString($"▭({rect.X},{rect.Y})", lf, lbrush, lx + 20, ly);
                ly += 14;
            }

            // Título
            using var titleF = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            using var titleB = new SolidBrush(Color.FromArgb(170, 180, 215));
            g.DrawString("Espectro de reflectancia / intensidad", titleF, titleB, padL, 4);

            _spectrumPlot.Image?.Dispose();
            _spectrumPlot.Image = bmp;

            // Info en la etiqueta
            double wlInfo = WavelengthAt(_currentBand);
            int totalSelections = _selectedPixels.Count + _selectedRects.Count;
            string selInfo = "";
            if (_selectedPixels.Count > 0) selInfo += $"{_selectedPixels.Count} punto(s)";
            if (_selectedRects.Count  > 0) selInfo += (selInfo.Length > 0 ? "  +  " : "") +
                                                       $"{_selectedRects.Count} área(s) (espectro promedio)";
            _lblSpectrumInfo.Text = $"  {selInfo}   │   " +
                $"banda {_currentBand + 1}/{_cube.Bands}  λ = {wlInfo:F1} {_cube.Header.WavelengthUnits}   │  Doble clic para limpiar";
        }

        // ── Helper: trazar una curva espectral ───────────────────────────────
        private static void DrawCurve(Graphics g, float[] spec, System.Collections.Generic.List<double> wls,
            Rectangle plot, double xMin, double xRng, float yMin, float yRange, Color color,
            bool dashed = false)
        {
            if (spec.Length < 2) return;
            var pts = new PointF[spec.Length];
            for (int i = 0; i < spec.Length; i++)
            {
                double wl = i < wls.Count ? wls[i] : xMin + i * xRng / Math.Max(1, spec.Length - 1);
                pts[i] = new PointF(Px(wl, xMin, xRng, plot), Py(spec[i], yMin, yRange, plot));
            }

            // Área bajo la curva (solo para píxeles, no para áreas para no saturar)
            if (!dashed)
            {
                var fill = new PointF[pts.Length + 2];
                fill[0] = new PointF(pts[0].X, plot.Bottom);
                pts.CopyTo(fill, 1);
                fill[^1] = new PointF(pts[^1].X, plot.Bottom);
                using var fb = new SolidBrush(Color.FromArgb(20, color.R, color.G, color.B));
                g.FillPolygon(fb, fill);
            }

            // Sombra
            using var sh = new Pen(Color.FromArgb(45, color.R, color.G, color.B), 3.5f);
            g.DrawLines(sh, pts);

            // Línea principal
            using var lp = new Pen(color, 1.8f) { LineJoin = LineJoin.Round };
            if (dashed) lp.DashStyle = DashStyle.Dash;
            g.DrawLines(lp, pts);
        }

        // ═════════════════════════════════════════════════════════════════════
        //  EXPORTAR
        // ═════════════════════════════════════════════════════════════════════

        private void BtnExport_Click(object? s, EventArgs e)
        {
            if (_currentBitmap == null) return;
            using var dlg = new SaveFileDialog
            {
                Filter   = "PNG (*.png)|*.png|TIFF (*.tif)|*.tif|BMP (*.bmp)|*.bmp",
                FileName = $"BLI_banda{_currentBand + 1:D3}_{WavelengthAt(_currentBand):F1}nm"
            };
            if (dlg.ShowDialog() == DialogResult.OK)
                _currentBitmap.Save(dlg.FileName, GetFmt(dlg.FileName));
        }

        private async void BtnExportAll_Click(object? s, EventArgs e)
        {
            if (_cube == null) return;
            using var dlg = new FolderBrowserDialog { Description = "Carpeta de salida" };
            if (dlg.ShowDialog() != DialogResult.OK) return;

            _btnExportAll.Enabled = false;
            var opts = BuildOptions();
            if (_grayscaleMode) opts.Colormap = BliColormap.Grayscale;

            await Task.Run(() =>
            {
                for (int b = 0; b < _cube.Bands; b++)
                {
                    opts.Wavelength = WavelengthAt(b);
                    using var bmp = BliRenderer.RenderBand(_cube, b, opts);
                    bmp.Save(Path.Combine(dlg.SelectedPath,
                             $"banda_{b + 1:D3}_{opts.Wavelength:F1}nm.png"), ImageFormat.Png);
                    Invoke(() =>
                    {
                        _progressBar.Visible = true;
                        _progressBar.Value   = (b + 1) * 100 / _cube.Bands;
                        _statusLabel.Text    = $"Exportando {b + 1} / {_cube.Bands}…";
                    });
                }
            });

            _progressBar.Visible  = false;
            _statusLabel.Text     = $"✔ Exportación completa — {_cube.Bands} bandas";
            _btnExportAll.Enabled = true;
        }

        // ═════════════════════════════════════════════════════════════════════
        //  UTILIDADES
        // ═════════════════════════════════════════════════════════════════════

        private BliRenderOptions BuildOptions() => new()
        {
            Colormap        = (BliColormap)_cmbColormap.SelectedIndex,
            Gamma           = (float)_nudGamma.Value,
            LowPercentile   = (float)_nudLowPct.Value,
            HighPercentile  = (float)_nudHighPct.Value,
            SignalThreshold = (float)_nudThreshold.Value,
            DrawColorbar    = _chkColorbar.Checked,
            Wavelength      = WavelengthAt(_currentBand),
            WavelengthUnit  = _cube?.Header.WavelengthUnits ?? "nm"
        };

        private double WavelengthAt(int band)
        {
            if (_cube == null) return double.NaN;
            return band < _cube.Header.Wavelengths.Count
                   ? _cube.Header.Wavelengths[band] : band;
        }

        private Point? MapToImage(Point screen)
        {
            if (_currentBitmap == null) return null;
            float scale = Math.Max(
                (float)_currentBitmap.Width  / _pictureBox.Width,
                (float)_currentBitmap.Height / _pictureBox.Height);
            float offX = (_pictureBox.Width  - _currentBitmap.Width  / scale) / 2f;
            float offY = (_pictureBox.Height - _currentBitmap.Height / scale) / 2f;
            return new Point(
                (int)((screen.X - offX) * scale),
                (int)((screen.Y - offY) * scale));
        }

        private static float Px(double wl, double xMin, double xRng, Rectangle plot) =>
            plot.Left + (float)((wl - xMin) / xRng * plot.Width);

        private static float Py(float v, float yMin, float yRng, Rectangle plot) =>
            Math.Clamp(plot.Bottom - (v - yMin) / yRng * plot.Height,
                       plot.Top - 8, plot.Bottom + 8);

        private static ImageFormat GetFmt(string p) =>
            Path.GetExtension(p).ToLower() switch
            {
                ".tif" or ".tiff" => ImageFormat.Tiff,
                ".bmp"            => ImageFormat.Bmp,
                _                 => ImageFormat.Png
            };

        private static string FmtVal(float v) =>
            Math.Abs(v) >= 10000 || (Math.Abs(v) < 0.001f && v != 0)
            ? v.ToString("0.0e0") : v.ToString("G4");
    }
}
