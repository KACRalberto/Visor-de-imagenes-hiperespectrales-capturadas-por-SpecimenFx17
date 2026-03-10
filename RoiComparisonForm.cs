using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace SpecimenFX17.Imaging
{
    public class RoiComparisonForm : Form
    {
        private readonly HyperspectralCube _origCube;
        private readonly HyperspectralCube _treatCube;
        private readonly List<SelectionShape> _rois;

        private PictureBox _picOrigImg = new();
        private PictureBox _picTreatImg = new();
        private PictureBox _picOrigPlot = new();
        private PictureBox _picTreatPlot = new();
        private TrackBar _slider = new();
        private Label _lblBand = new();
        private int _currentBand;

        public RoiComparisonForm(HyperspectralCube origCube, HyperspectralCube treatCube, List<SelectionShape> rois, int startBand)
        {
            _origCube = origCube;
            _treatCube = treatCube;
            _rois = rois;

            _currentBand = Math.Clamp(startBand, 0, origCube.Bands - 1);

            Text = "Comparativa de Tratamiento Quimiométrico (Antes vs Después)";
            Size = new Size(1200, 800);
            BackColor = Color.FromArgb(18, 18, 26);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9f);

            BuildUI();
            RefreshAll();
        }

        private void BuildUI()
        {
            var tlp = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3
            };
            tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
            tlp.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            tlp.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

            tlp.Controls.Add(CreateTitle("ESPECTRO ORIGINAL / RAW"), 0, 0);
            tlp.Controls.Add(CreateTitle("ESPECTRO TRATADO / FILTRADO"), 1, 0);

            _picOrigImg.Dock = _picTreatImg.Dock = DockStyle.Fill;
            _picOrigImg.SizeMode = _picTreatImg.SizeMode = PictureBoxSizeMode.Zoom;
            _picOrigImg.BackColor = _picTreatImg.BackColor = Color.Black;
            tlp.Controls.Add(_picOrigImg, 0, 1);
            tlp.Controls.Add(_picTreatImg, 1, 1);

            _picOrigPlot.Dock = _picTreatPlot.Dock = DockStyle.Fill;
            _picOrigPlot.BackColor = _picTreatPlot.BackColor = Color.FromArgb(12, 12, 20);

            // SOLUCIÓN AL CRASHEO: Dibujado gestionado por Windows (evento Paint)
            _picOrigPlot.Paint += (s, e) => DrawPlot(e.Graphics, _picOrigPlot, _origCube);
            _picTreatPlot.Paint += (s, e) => DrawPlot(e.Graphics, _picTreatPlot, _treatCube);
            _picOrigPlot.Resize += (s, e) => _picOrigPlot.Invalidate();
            _picTreatPlot.Resize += (s, e) => _picTreatPlot.Invalidate();

            tlp.Controls.Add(_picOrigPlot, 0, 2);
            tlp.Controls.Add(_picTreatPlot, 1, 2);

            var pnlBot = new Panel { Dock = DockStyle.Bottom, Height = 50, BackColor = Color.FromArgb(22, 22, 34) };

            _lblBand = new Label { Dock = DockStyle.Bottom, Height = 18, ForeColor = Color.LightSkyBlue, Font = new Font("Consolas", 9f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
            _slider = new TrackBar { Dock = DockStyle.Fill, Minimum = 0, Maximum = _origCube.Bands - 1, Value = _currentBand, TickStyle = TickStyle.None };
            _slider.Scroll += (_, _) => { _currentBand = _slider.Value; RefreshAll(); };

            pnlBot.Controls.Add(_slider);
            pnlBot.Controls.Add(_lblBand);

            Controls.Add(tlp);
            Controls.Add(pnlBot);
        }

        private Label CreateTitle(string text) => new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Color.FromArgb(180, 200, 220),
            BackColor = Color.FromArgb(26, 26, 38)
        };

        private void RefreshAll()
        {
            double wl = _origCube.Header.Wavelengths.Count > _currentBand ? _origCube.Header.Wavelengths[_currentBand] : 0;
            _lblBand.Text = $"Viendo Banda {_currentBand + 1}  —  Longitud de onda: {wl:F1} nm";

            // SOLUCIÓN IMAGEN OSCURA: Se usan percentiles 2-98 para obviar valores extremos y visualizar bien
            var optsOrig = new BliRenderOptions { Colormap = BliColormap.Grayscale, Wavelength = wl, LowPercentile = 2f, HighPercentile = 98f };
            var optsTreat = new BliRenderOptions { Colormap = BliColormap.Grayscale, Wavelength = wl, LowPercentile = 2f, HighPercentile = 98f };

            var old1 = _picOrigImg.Image; _picOrigImg.Image = BliRenderer.RenderBand(_origCube, _currentBand, optsOrig); old1?.Dispose();
            var old2 = _picTreatImg.Image; _picTreatImg.Image = BliRenderer.RenderBand(_treatCube, _currentBand, optsTreat); old2?.Dispose();

            OverlayRois(_picOrigImg.Image);
            OverlayRois(_picTreatImg.Image);

            _picOrigPlot.Invalidate();
            _picTreatPlot.Invalidate();
        }

        private void OverlayRois(Image img)
        {
            using var g = Graphics.FromImage(img);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (var roi in _rois) roi.DrawOn(g);
        }

        private void DrawPlot(Graphics g, PictureBox pic, HyperspectralCube cube)
        {
            // PROTECCIÓN EXTREMA DE LAYOUT
            if (pic.Width < 50 || pic.Height < 50) return;

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.FromArgb(12, 12, 20));

            Rectangle plot = new Rectangle(50, 20, pic.Width - 70, pic.Height - 50);

            using (var gp = new Pen(Color.FromArgb(30, 255, 255, 255), 1f) { DashStyle = DashStyle.Dash })
            {
                for (int i = 0; i <= 4; i++) g.DrawLine(gp, plot.Left, plot.Bottom - i * plot.Height / 4f, plot.Right, plot.Bottom - i * plot.Height / 4f);
                for (int i = 0; i <= 6; i++) g.DrawLine(gp, plot.Left + i * plot.Width / 6f, plot.Top, plot.Left + i * plot.Width / 6f, plot.Bottom);
            }
            g.DrawRectangle(new Pen(Color.FromArgb(70, 255, 255, 255)), plot);

            float yMin = float.MaxValue, yMax = float.MinValue;
            var specs = _rois.Select(r => r.GetSpectrum(cube).Take(cube.Header.Bands).ToArray()).ToList();

            // Protección contra valores nulos o infinitos
            foreach (var s in specs)
            {
                foreach (var v in s)
                {
                    if (!float.IsNaN(v) && !float.IsInfinity(v))
                    {
                        if (v < yMin) yMin = v;
                        if (v > yMax) yMax = v;
                    }
                }
            }
            if (yMin == float.MaxValue) { yMin = 0; yMax = 1; }
            if (Math.Abs(yMin - yMax) < 1e-6f) { yMin -= 0.5f; yMax += 0.5f; } // Evita divisiones por cero

            float yRng = yMax - yMin;
            yMin -= yRng * 0.05f; yMax += yRng * 0.05f; yRng = yMax - yMin;

            double xMin = cube.Header.Wavelengths.FirstOrDefault(), xMax = cube.Header.Wavelengths.LastOrDefault(), xRng = xMax - xMin;
            if (xRng == 0) xRng = 1;

            double curWl = cube.Header.Wavelengths.Count > _currentBand ? cube.Header.Wavelengths[_currentBand] : 0;
            float curPx = plot.Left + (float)((curWl - xMin) / xRng * plot.Width);
            g.DrawLine(new Pen(Color.FromArgb(110, 255, 255, 80), 1f) { DashStyle = DashStyle.Dash }, curPx, plot.Top, curPx, plot.Bottom);

            float safeClampMin = Math.Min(plot.Top - 10, plot.Bottom + 10);
            float safeClampMax = Math.Max(plot.Top - 10, plot.Bottom + 10);

            for (int r = 0; r < _rois.Count; r++)
            {
                var s = specs[r];
                var pts = new List<PointF>();
                for (int i = 0; i < s.Length; i++)
                {
                    if (float.IsNaN(s[i]) || float.IsInfinity(s[i])) continue;
                    float px = plot.Left + (float)(((cube.Header.Wavelengths[i]) - xMin) / xRng * plot.Width);
                    float rawPy = plot.Bottom - (s[i] - yMin) / yRng * plot.Height;
                    float py = Math.Clamp(rawPy, safeClampMin, safeClampMax);
                    pts.Add(new PointF(px, py));
                }
                if (pts.Count > 1) g.DrawLines(new Pen(_rois[r].Color, 2f), pts.ToArray());
            }

            using var tf = new Font("Consolas", 7.5f); using var tb = new SolidBrush(Color.FromArgb(160, 160, 195));
            g.DrawString(yMax.ToString("G4"), tf, tb, plot.Left - 45, plot.Top - 6);
            g.DrawString(yMin.ToString("G4"), tf, tb, plot.Left - 45, plot.Bottom - 6);
            g.DrawString(xMin.ToString("F0"), tf, tb, plot.Left - 10, plot.Bottom + 5);
            g.DrawString(xMax.ToString("F0"), tf, tb, plot.Right - 20, plot.Bottom + 5);
        }
    }
}