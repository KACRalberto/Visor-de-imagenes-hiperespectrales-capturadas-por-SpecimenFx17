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

        private TrackBar _trkThresh, _trkOpen, _trkClose, _trkArea;
        private CheckBox _chkInvert;

        public InteractiveSegmentationForm(HyperspectralCube cube, int band)
        {
            _cube = cube;
            _band = band;
            Text = "UltraVisor - Ajuste Interactivo de Segmentación";
            Size = new System.Drawing.Size(1000, 700);
            BackColor = Color.FromArgb(30, 30, 30);
            ForeColor = Color.White;

            BuildUI();
            UpdatePreview();
        }

        private void BuildUI()
        {
            var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 300 };

            var pnlControls = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(10) };

            _chkInvert = new CheckBox { Text = "Invertir Umbral (Si el fondo es más claro que el objeto)", AutoSize = true, ForeColor = Color.LightSkyBlue, Margin = new Padding(3, 10, 3, 10) };
            _chkInvert.CheckedChanged += (s, e) => { Params.InvertThreshold = _chkInvert.Checked; UpdatePreview(); };
            pnlControls.Controls.Add(_chkInvert);

            _trkThresh = AddSlider(pnlControls, "Umbral de Intensidad", 0, 255, Params.Threshold);
            _trkOpen = AddSlider(pnlControls, "Apertura (Limpiar Ruido Exterior)", 0, 10, Params.OpenIters);
            _trkClose = AddSlider(pnlControls, "Cierre (Rellenar Huecos Internos)", 0, 10, Params.CloseIters);
            _trkArea = AddSlider(pnlControls, "Área Mínima (Descartar motas)", 10, 10000, Params.MinArea);

            var btnConfirm = new Button { Text = "✅ Validar y Continuar", BackColor = Color.MediumSeaGreen, ForeColor = Color.White, Width = 250, Height = 40, Margin = new Padding(0, 20, 0, 0) };
            btnConfirm.Click += (s, e) => { this.DialogResult = DialogResult.OK; this.Close(); };
            pnlControls.Controls.Add(btnConfirm);

            split.Panel1.Controls.Add(pnlControls);

            _picPreview = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
            split.Panel2.Controls.Add(_picPreview);

            Controls.Add(split);
        }

        private TrackBar AddSlider(FlowLayoutPanel pnl, string label, int min, int max, int val)
        {
            pnl.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = Color.LightGray });
            var trk = new TrackBar { Minimum = min, Maximum = max, Value = val, Width = 260, TickStyle = TickStyle.None };
            trk.Scroll += (s, e) =>
            {
                Params.Threshold = _trkThresh.Value;
                Params.OpenIters = _trkOpen.Value;
                Params.CloseIters = _trkClose.Value;
                Params.MinArea = _trkArea.Value;
                UpdatePreview();
            };
            pnl.Controls.Add(trk);
            return trk;
        }

        private void UpdatePreview()
        {
            using Mat gray = new Mat(_cube.Lines, _cube.Samples, MatType.CV_8UC1);
            var indexer = gray.GetGenericIndexer<byte>();
            float minV = float.MaxValue, maxV = float.MinValue;

            for (int y = 0; y < _cube.Lines; y++) for (int x = 0; x < _cube.Samples; x++) { float v = _cube[_band, y, x]; if (!float.IsNaN(v)) { if (v < minV) minV = v; if (v > maxV) maxV = v; } }
            float range = maxV - minV <= 0 ? 1 : maxV - minV;

            for (int y = 0; y < _cube.Lines; y++) for (int x = 0; x < _cube.Samples; x++) indexer[y, x] = float.IsNaN(_cube[_band, y, x]) ? (byte)0 : (byte)(((_cube[_band, y, x] - minV) / range) * 255);

            // AQUÍ ESTÁ LA MAGIA: Llamamos a la MISMA función matemática que usa el Batch
            using Mat mask = AutoSegmenter.GetRawMask(gray, Params);

            using Mat rgb = new Mat();
            Cv2.CvtColor(gray, rgb, ColorConversionCodes.GRAY2BGR);
            rgb.SetTo(new Scalar(0, 0, 255), mask); // Pinta el área segmentada en Rojo brillante

            using (var ms = rgb.ToMemoryStream(".png"))
            {
                _picPreview.Image?.Dispose();
                _picPreview.Image = new Bitmap(ms);
            }
        }
    }
}