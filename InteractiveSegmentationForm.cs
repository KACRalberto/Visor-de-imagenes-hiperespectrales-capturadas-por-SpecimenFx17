using System;
using System.Drawing;
using System.Windows.Forms;
using OpenCvSharp;

namespace SpecimenFX17.Imaging
{
    public class InteractiveSegmentationForm : Form
    {
        private HyperspectralCube _cube;
        private int _band;
        private PictureBox _picPreview;
        public SegmentationParams Params { get; } = new SegmentationParams();

        private TrackBar _trkBlockSize, _trkConstantC;
        private TrackBar _trkOpen, _trkClose, _trkArea;
        private CheckBox _chkInvert;
        private Label _lblClicks;

        public InteractiveSegmentationForm(HyperspectralCube cube, int band)
        {
            _cube = cube;
            _band = band;
            Text = "UltraVisor Industrial - Ajuste de Segmentación (Clic Derecho en la imagen para Borrar Sombras/Cinta)";
            Size = new System.Drawing.Size(1200, 800);
            BackColor = Color.FromArgb(30, 30, 30);
            ForeColor = Color.White;

            BuildUI();
            UpdatePreview();
        }

        private void BuildUI()
        {
            var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 320 };

            var pnlControls = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(10) };

            _chkInvert = new CheckBox { Text = "Invertir Umbral (Si el fondo es brillante)", AutoSize = true, ForeColor = Color.LightSkyBlue, Margin = new Padding(3, 10, 3, 10) };
            _chkInvert.CheckedChanged += (s, e) => { Params.InvertThreshold = _chkInvert.Checked; UpdatePreview(); };
            pnlControls.Controls.Add(_chkInvert);

            _trkBlockSize = AddSlider(pnlControls, "Filtro Local (Block Size) [Impar]", 3, 101, Params.BlockSize);
            _trkBlockSize.TickStyle = TickStyle.None;

            _trkConstantC = AddSlider(pnlControls, "Sensibilidad Local (Constante C)", -50, 50, (int)Params.ConstantC);
            _trkConstantC.TickStyle = TickStyle.None;

            _trkOpen = AddSlider(pnlControls, "Apertura (Limpiar Ruido Exterior)", 0, 15, Params.OpenIters);
            _trkClose = AddSlider(pnlControls, "Cierre (Rellenar Huecos Internos)", 0, 15, Params.CloseIters);
            _trkArea = AddSlider(pnlControls, "Área Mínima (Descartar motas)", 10, 15000, Params.MinArea);
            _trkArea.TickStyle = TickStyle.None;

            _lblClicks = new Label { Text = "Borrados manuales: 0", AutoSize = true, ForeColor = Color.LightCoral, Margin = new Padding(3, 15, 3, 5) };
            var btnClearClicks = new Button { Text = "🔄 Deshacer Borrados (Clics)", BackColor = Color.FromArgb(80, 40, 40), ForeColor = Color.White, Width = 260, Height = 35 };
            btnClearClicks.Click += (s, e) => { Params.PointsToRemove.Clear(); UpdatePreview(); };

            pnlControls.Controls.Add(_lblClicks);
            pnlControls.Controls.Add(btnClearClicks);

            var btnConfirm = new Button { Text = "✅ Validar y Continuar", BackColor = Color.MediumSeaGreen, ForeColor = Color.White, Width = 260, Height = 40, Margin = new Padding(0, 25, 0, 0) };
            btnConfirm.Click += (s, e) => { this.DialogResult = DialogResult.OK; this.Close(); };
            pnlControls.Controls.Add(btnConfirm);

            split.Panel1.Controls.Add(pnlControls);

            _picPreview = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black, Cursor = Cursors.Cross };

            // EVENTO DE CLIC DERECHO PARA BORRAR ZONAS INDESEADAS
            _picPreview.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    var imgPt = MapToImage(e.Location);
                    if (imgPt.HasValue)
                    {
                        Params.PointsToRemove.Add(imgPt.Value);
                        UpdatePreview();
                    }
                }
            };

            split.Panel2.Controls.Add(_picPreview);
            Controls.Add(split);
        }

        private TrackBar AddSlider(FlowLayoutPanel pnl, string label, int min, int max, int val)
        {
            pnl.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = Color.LightGray });
            var trk = new TrackBar { Minimum = min, Maximum = max, Value = val, Width = 280 };
            trk.Scroll += (s, e) =>
            {
                Params.BlockSize = _trkBlockSize.Value | 1; // Fuerza a ser impar
                Params.ConstantC = _trkConstantC.Value;
                Params.OpenIters = _trkOpen.Value;
                Params.CloseIters = _trkClose.Value;
                Params.MinArea = _trkArea.Value;
                UpdatePreview();
            };
            pnl.Controls.Add(trk);
            return trk;
        }

        // Mapeo preciso de Pantalla a Imagen Original
        private System.Drawing.Point? MapToImage(System.Drawing.Point mousePos)
        {
            if (_picPreview.Image == null) return null;

            float picRatio = (float)_picPreview.Width / _picPreview.Height;
            float imgRatio = (float)_cube.Samples / _cube.Lines;

            float drawWidth = _picPreview.Width;
            float drawHeight = _picPreview.Height;
            float offsetX = 0, offsetY = 0;

            if (picRatio > imgRatio)
            {
                drawWidth = _picPreview.Height * imgRatio;
                offsetX = (_picPreview.Width - drawWidth) / 2f;
            }
            else
            {
                drawHeight = _picPreview.Width / imgRatio;
                offsetY = (_picPreview.Height - drawHeight) / 2f;
            }

            float x = mousePos.X - offsetX;
            float y = mousePos.Y - offsetY;

            if (x < 0 || x >= drawWidth || y < 0 || y >= drawHeight) return null;

            int imgX = (int)((x / drawWidth) * _cube.Samples);
            int imgY = (int)((y / drawHeight) * _cube.Lines);

            return new System.Drawing.Point(imgX, imgY);
        }

        private void UpdatePreview()
        {
            _lblClicks.Text = $"Borrados manuales: {Params.PointsToRemove.Count}";

            using Mat gray = new Mat(_cube.Lines, _cube.Samples, MatType.CV_8UC1);
            var indexer = gray.GetGenericIndexer<byte>();
            float minV = float.MaxValue, maxV = float.MinValue;

            for (int y = 0; y < _cube.Lines; y++) for (int x = 0; x < _cube.Samples; x++) { float v = _cube[_band, y, x]; if (!float.IsNaN(v)) { if (v < minV) minV = v; if (v > maxV) maxV = v; } }
            float range = maxV - minV <= 0 ? 1 : maxV - minV;

            for (int y = 0; y < _cube.Lines; y++) for (int x = 0; x < _cube.Samples; x++) indexer[y, x] = float.IsNaN(_cube[_band, y, x]) ? (byte)0 : (byte)(((_cube[_band, y, x] - minV) / range) * 255);

            using Mat mask = AutoSegmenter.GetRawMask(gray, Params);

            using Mat rgb = new Mat();
            Cv2.CvtColor(gray, rgb, ColorConversionCodes.GRAY2BGR);
            rgb.SetTo(new Scalar(0, 0, 255), mask); // Pinta en rojo los objetos

            // Dibuja X verdes donde el usuario haya borrado
            foreach (var pt in Params.PointsToRemove)
            {
                Cv2.DrawMarker(rgb, new OpenCvSharp.Point(pt.X, pt.Y), new Scalar(0, 255, 0), MarkerTypes.Cross, 20, 2);
            }

            using (var ms = rgb.ToMemoryStream(".png"))
            {
                _picPreview.Image?.Dispose();
                _picPreview.Image = new Bitmap(ms);
            }
        }
    }
}