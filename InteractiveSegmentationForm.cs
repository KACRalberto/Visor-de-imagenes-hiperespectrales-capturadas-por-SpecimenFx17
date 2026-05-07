using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenCvSharp;

namespace SpecimenFX17.Imaging
{
    public class InteractiveSegmentationForm : Form
    {
        public SegmentationParams Params { get; private set; } = new();
        private readonly HyperspectralCube _cube;
        private readonly int _band;

        private PictureBox _picPreview = null!;
        private Label _lblStatus = null!;

        private TrackBar _trkThreshold = null!;
        private CheckBox _chkInvert = null!;
        private TrackBar _trkArea = null!;
        private TrackBar _trkClose = null!;
        private TrackBar _trkOpen = null!;
        private TrackBar _trkTop = null!;
        private TrackBar _trkBottom = null!;

        private Label _lblThreshold = null!;
        private Label _lblArea = null!;
        private Label _lblClose = null!;
        private Label _lblOpen = null!;
        private Label _lblTop = null!;
        private Label _lblBottom = null!;

        private CancellationTokenSource? _cts;
        private Mat? _gray8U;
        private Bitmap? _previewBmp;
        private bool _isDrawing = false;

        public InteractiveSegmentationForm(HyperspectralCube cube, int band)
        {
            _cube = cube;
            _band = band;

            Text = "UltraVisor de Segmentación - Modo Blindado (Ver. 2)";
            Size = new System.Drawing.Size(1200, 850);
            BackColor = Color.FromArgb(30, 30, 35);
            ForeColor = Color.White;
            StartPosition = FormStartPosition.CenterParent;

            BuildUI();
            InitializeImage();
        }

        private async void InitializeImage()
        {
            _lblStatus.Text = "⏳ Extrayendo banda hiperespectral y normalizando...";
            _lblStatus.ForeColor = Color.Yellow;

            Params.StretchMin = float.NaN;
            Params.StretchMax = float.NaN;

            try
            {
                _gray8U = await Task.Run(() => AutoSegmenter.NormalizeBandTo8Bit(_cube, _band, Params, CancellationToken.None));
                UpdatePreview();
            }
            catch (Exception ex)
            {
                _lblStatus.Text = $"❌ Error crítico de carga: {ex.Message}";
                _lblStatus.ForeColor = Color.Red;
            }
        }

        private void BuildUI()
        {
            var pnlControls = new Panel { Dock = DockStyle.Right, Width = 380, BackColor = Color.FromArgb(25, 25, 30), Padding = new Padding(15) };
            var flp = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };

            var lblTitle = new Label { Text = "CONTROLES DE DETECCIÓN", AutoSize = true, Font = new Font("Segoe UI", 12f, FontStyle.Bold), ForeColor = Color.LightSkyBlue, Margin = new Padding(0, 0, 0, 10) };

            var lblHelp = new Label
            {
                Text = "💡 VARITA: Clic IZQUIERDO mantenido pinta y repara brillos.\n❌ BORRAR: Clic DERECHO en manchas a eliminar.",
                AutoSize = true,
                ForeColor = Color.Orange,
                Font = new Font("Segoe UI", 9f),
                Margin = new Padding(0, 0, 0, 15)
            };

            _chkInvert = new CheckBox { Text = "Invertir Umbral (Detectar Oscuros)", AutoSize = true, ForeColor = Color.White, Checked = false, Margin = new Padding(0, 0, 0, 15) };
            _chkInvert.CheckedChanged += (s, e) => { Params.InvertThreshold = _chkInvert.Checked; UpdatePreview(); };

            _lblThreshold = new Label { Text = "1. Umbral de corte: 154", AutoSize = true, Margin = new Padding(0, 5, 0, 0) };
            _trkThreshold = new TrackBar { Minimum = 0, Maximum = 255, Value = 154, Width = 320, TickFrequency = 15 };
            _trkThreshold.Scroll += (s, e) => { Params.Threshold = _trkThreshold.Value; _lblThreshold.Text = $"1. Umbral de corte: {Params.Threshold}"; UpdatePreview(); };

            _lblClose = new Label { Text = "2. Cierre morfológico: 2", AutoSize = true, Margin = new Padding(0, 15, 0, 0) };
            _trkClose = new TrackBar { Minimum = 0, Maximum = 10, Value = 2, Width = 320, TickFrequency = 1 };
            _trkClose.Scroll += (s, e) => { Params.CloseIters = _trkClose.Value; _lblClose.Text = $"2. Cierre morfológico: {Params.CloseIters}"; UpdatePreview(); };

            _lblOpen = new Label { Text = "3. Apertura morfológica: 1", AutoSize = true, Margin = new Padding(0, 15, 0, 0) };
            _trkOpen = new TrackBar { Minimum = 0, Maximum = 10, Value = 1, Width = 320, TickFrequency = 1 };
            _trkOpen.Scroll += (s, e) => { Params.OpenIters = _trkOpen.Value; _lblOpen.Text = $"3. Apertura morfológica: {Params.OpenIters}"; UpdatePreview(); };

            _lblArea = new Label { Text = "4. Tamaño Mínimo (Píxeles): 100", AutoSize = true, Margin = new Padding(0, 15, 0, 0), ForeColor = Color.LightGreen };
            _trkArea = new TrackBar { Minimum = 10, Maximum = 10000, Value = 100, Width = 320, TickFrequency = 500 };
            _trkArea.Scroll += (s, e) => { Params.MinArea = _trkArea.Value; _lblArea.Text = $"4. Tamaño Mínimo (Píxeles): {Params.MinArea}"; UpdatePreview(); };

            _lblTop = new Label { Text = "5. Ignorar Margen Superior (%): 0", AutoSize = true, Margin = new Padding(0, 15, 0, 0), ForeColor = Color.Yellow };
            _trkTop = new TrackBar { Minimum = 0, Maximum = 40, Value = 0, Width = 320, TickFrequency = 5 };
            _trkTop.Scroll += (s, e) => { Params.IgnoreTopPct = _trkTop.Value; _lblTop.Text = $"5. Ignorar Margen Superior (%): {Params.IgnoreTopPct}"; UpdatePreview(); };

            _lblBottom = new Label { Text = "6. Ignorar Margen Inferior (%): 0", AutoSize = true, Margin = new Padding(0, 5, 0, 0), ForeColor = Color.Yellow };
            _trkBottom = new TrackBar { Minimum = 0, Maximum = 40, Value = 0, Width = 320, TickFrequency = 5 };
            _trkBottom.Scroll += (s, e) => { Params.IgnoreBottomPct = _trkBottom.Value; _lblBottom.Text = $"6. Ignorar Margen Inferior (%): {Params.IgnoreBottomPct}"; UpdatePreview(); };

            var btnClear = new Button { Text = "Limpiar clics y pinceladas", Width = 320, Height = 40, BackColor = Color.FromArgb(60, 60, 65), FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 20, 0, 0) };
            btnClear.Click += (s, e) => { Params.PointsToRemove.Clear(); Params.PointsToRepair.Clear(); UpdatePreview(); };

            var btnOk = new Button { Text = "✔ VALIDAR Y APLICAR", Dock = DockStyle.Bottom, Height = 60, BackColor = Color.FromArgb(40, 150, 80), Font = new Font("Segoe UI", 10f, FontStyle.Bold), FlatStyle = FlatStyle.Flat };
            btnOk.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };

            flp.Controls.Add(lblTitle);
            flp.Controls.Add(lblHelp);
            flp.Controls.Add(_chkInvert);
            flp.Controls.Add(_lblThreshold);
            flp.Controls.Add(_trkThreshold);
            flp.Controls.Add(_lblClose);
            flp.Controls.Add(_trkClose);
            flp.Controls.Add(_lblOpen);
            flp.Controls.Add(_trkOpen);
            flp.Controls.Add(_lblArea);
            flp.Controls.Add(_trkArea);
            flp.Controls.Add(_lblTop);
            flp.Controls.Add(_trkTop);
            flp.Controls.Add(_lblBottom);
            flp.Controls.Add(_trkBottom);
            flp.Controls.Add(btnClear);

            pnlControls.Controls.Add(flp);
            pnlControls.Controls.Add(btnOk);

            var pnlImage = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };

            // EL CHIVATO AHORA ESTÁ ARRIBA Y FLOTANDO PARA QUE NUNCA SE OCULTE
            _lblStatus = new Label
            {
                Text = "Inicializando...",
                Dock = DockStyle.Top,
                Height = 35,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                BackColor = Color.FromArgb(200, 20, 20, 25)
            };

            _picPreview = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Cursor = Cursors.Default };

            _picPreview.MouseDown += PicPreview_MouseDown;
            _picPreview.MouseMove += PicPreview_MouseMove;
            _picPreview.MouseUp += (s, e) => { _isDrawing = false; };

            pnlImage.Controls.Add(_picPreview);
            _picPreview.Controls.Add(_lblStatus); // Añadido dentro de la foto

            Controls.Add(pnlImage);
            Controls.Add(pnlControls);

            Params.Threshold = _trkThreshold.Value;
            Params.InvertThreshold = _chkInvert.Checked;
            Params.CloseIters = _trkClose.Value;
            Params.OpenIters = _trkOpen.Value;
            Params.MinArea = _trkArea.Value;
        }

        private void PicPreview_MouseDown(object? sender, MouseEventArgs e)
        {
            if (_previewBmp == null) return;

            var imgCoords = MapClientToImage(e.X, e.Y);
            if (!imgCoords.HasValue) return;
            var pt = imgCoords.Value;

            if (e.Button == MouseButtons.Left)
            {
                _isDrawing = true;
                Params.PointsToRepair.Add(pt);
                UpdatePreview();
            }
            else if (e.Button == MouseButtons.Right)
            {
                Params.PointsToRemove.Add(pt);
                UpdatePreview();
            }
        }

        private void PicPreview_MouseMove(object? sender, MouseEventArgs e)
        {
            if (!_isDrawing || _previewBmp == null) return;
            var imgCoords = MapClientToImage(e.X, e.Y);
            if (imgCoords.HasValue)
            {
                Params.PointsToRepair.Add(imgCoords.Value);
                UpdatePreview();
            }
        }

        private System.Drawing.Point? MapClientToImage(int clientX, int clientY)
        {
            if (_previewBmp == null) return null;

            float ratioX = (float)_picPreview.Width / _previewBmp.Width;
            float ratioY = (float)_picPreview.Height / _previewBmp.Height;
            float ratio = Math.Min(ratioX, ratioY);

            int offX = (int)((_picPreview.Width - (_previewBmp.Width * ratio)) / 2);
            int offY = (int)((_picPreview.Height - (_previewBmp.Height * ratio)) / 2);

            int imgX = (int)((clientX - offX) / ratio);
            int imgY = (int)((clientY - offY) / ratio);

            if (imgX >= 0 && imgX < _cube.Samples && imgY >= 0 && imgY < _cube.Lines)
                return new System.Drawing.Point(imgX, imgY);

            return null;
        }

        private async void UpdatePreview()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            // Para no saturar visualmente, solo mostramos procesando si tarda de verdad
            _lblStatus.Text = "⏳ Procesando Segmentación...";
            _lblStatus.ForeColor = Color.Orange;

            try
            {
                await Task.Run(() =>
                {
                    if (_gray8U == null) return;

                    using Mat rawMask = AutoSegmenter.GetRawMask(_gray8U, Params);
                    token.ThrowIfCancellationRequested();

                    using Mat filteredMask = Mat.Zeros(rawMask.Size(), MatType.CV_8UC1);
                    using Mat labels = new Mat();
                    using Mat stats = new Mat();
                    using Mat centroids = new Mat();

                    int numLabels = Cv2.ConnectedComponentsWithStats(rawMask, labels, stats, centroids);
                    var labelIndexer = labels.GetGenericIndexer<int>();
                    var maskIndexer = filteredMask.GetGenericIndexer<byte>();

                    HashSet<int> validLabels = new HashSet<int>();
                    for (int i = 1; i < numLabels; i++)
                    {
                        if (stats.At<int>(i, (int)ConnectedComponentsTypes.Area) >= Params.MinArea)
                            validLabels.Add(i);
                    }

                    token.ThrowIfCancellationRequested();

                    for (int y = 0; y < rawMask.Rows; y++)
                    {
                        for (int x = 0; x < rawMask.Cols; x++)
                        {
                            if (validLabels.Contains(labelIndexer[y, x]))
                                maskIndexer[y, x] = 255;
                        }
                    }

                    token.ThrowIfCancellationRequested();

                    using Mat colorView = new Mat();
                    Cv2.CvtColor(_gray8U, colorView, ColorConversionCodes.GRAY2BGR);
                    colorView.SetTo(new Scalar(0, 0, 255), filteredMask);

                    Bitmap newBmp;
                    using (var tempBmp = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(colorView))
                    {
                        newBmp = new Bitmap(tempBmp);
                    }

                    this.Invoke(new Action(() => {
                        var oldImg = _picPreview.Image;
                        _previewBmp = newBmp;
                        _picPreview.Image = _previewBmp;
                        oldImg?.Dispose();

                        _lblStatus.Text = "✅ Listo";
                        _lblStatus.ForeColor = Color.LightGreen;
                    }));

                }, token);
            }
            catch (OperationCanceledException)
            {
                // Ignoramos la cancelación limpia
            }
            catch (Exception ex)
            {
                // YA NO QUEDAN ERRORES SILENCIADOS. SI ALGO FALLA, LO VERÁS AQUÍ.
                this.Invoke(new Action(() => {
                    _lblStatus.Text = $"❌ Error: {ex.Message}";
                    _lblStatus.ForeColor = Color.Red;
                }));
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _cts?.Cancel();
            _gray8U?.Dispose();
            _previewBmp?.Dispose();
            base.OnFormClosing(e);
        }
    }
}