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
            double wl = _origCube.Header.Wavelengths != null && _origCube.Header.Wavelengths.Count > _currentBand ? _origCube.Header.Wavelengths[_currentBand] : _currentBand;
            _lblBand.Text = $"Viendo Banda {_currentBand + 1}  —  Longitud de onda: {wl:F1} nm";

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
            if (pic.Width < 50 || pic.Height < 50) return;

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(Color.FromArgb(12, 12, 20));

            var rect = new Rectangle(60, 20, pic.Width - 80, pic.Height - 60);

            using (var gp = new Pen(Color.FromArgb(28, 255, 255, 255), 1f) { DashStyle = DashStyle.Dot })
            {
                for (int i = 0; i <= 5; i++) g.DrawLine(gp, rect.Left, rect.Bottom - (float)i / 5 * rect.Height, rect.Right, rect.Bottom - (float)i / 5 * rect.Height);
                for (int i = 0; i <= 6; i++) g.DrawLine(gp, rect.Left + (float)i / 6 * rect.Width, rect.Top, rect.Left + (float)i / 6 * rect.Width, rect.Bottom);
            }
            using (var bp = new Pen(Color.FromArgb(60, 255, 255, 255))) g.DrawRectangle(bp, rect);

            float yMin = float.MaxValue, yMax = float.MinValue;
            var plotData = new List<(float[] spec, Color col, string name)>();

            foreach (var roi in _rois)
            {
                var spec = roi.GetSpectrum(cube).ToArray();
                if (spec.Length == 0) continue;

                plotData.Add((spec, roi.Color, roi.ShortLabel));

                foreach (var v in spec)
                {
                    if (!float.IsNaN(v) && !float.IsInfinity(v))
                    {
                        if (v < yMin) yMin = v;
                        if (v > yMax) yMax = v;
                    }
                }
            }

            if (yMin == float.MaxValue) { yMin = 0; yMax = 1; }
            float yRng = yMax - yMin;
            if (yRng < 1e-10f) yRng = 1f;
            yMin -= yRng * 0.05f; yMax += yRng * 0.05f; yRng = yMax - yMin;

            // BUG 10 SOLUCIONADO: Validación segura si Wavelengths es nulo, vacío o tiene menos elementos que bandas
            var wls = cube.Header.Wavelengths;
            int bands = cube.Bands;
            double xMin = wls != null && wls.Count > 0 ? wls[0] : 0;
            double xMax = wls != null && wls.Count > 0 ? wls[^1] : bands - 1;
            double xRng = xMax - xMin;
            if (xRng == 0) xRng = 1;

            foreach (var (spec, col, name) in plotData)
            {
                var pts = new List<PointF>();
                int len = Math.Min(spec.Length, bands);
                for (int i = 0; i < len; i++)
                {
                    if (float.IsNaN(spec[i]) || float.IsInfinity(spec[i])) continue;

                    double currentWl = wls != null && wls.Count > i ? wls[i] : xMin + i * (xRng / len);
                    float px = rect.Left + (float)((currentWl - xMin) / xRng * rect.Width);
                    float py = rect.Bottom - ((spec[i] - yMin) / yRng * rect.Height);

                    // BUG 9 SOLUCIONADO: Prevención de crash GDI+ limitando coordenadas de dibujo
                    py = Math.Clamp(py, rect.Top - 5000, rect.Bottom + 5000);
                    pts.Add(new PointF(px, py));
                }

                if (pts.Count > 1)
                {
                    using var pen = new Pen(col, 2f) { LineJoin = LineJoin.Round };
                    g.DrawLines(pen, pts.ToArray());
                }
            }

            using var font = new Font("Consolas", 8f);
            using var brush = new SolidBrush(Color.FromArgb(180, 180, 200));

            for (int i = 0; i <= 5; i++)
            {
                float py = rect.Bottom - (float)i / 5 * rect.Height;
                float val = yMin + yRng * i / 5;
                string lb = val.ToString("G4");
                var sz = g.MeasureString(lb, font);
                g.DrawString(lb, font, brush, rect.Left - sz.Width - 5, py - sz.Height / 2);
            }

            for (int i = 0; i <= 6; i++)
            {
                float px = rect.Left + (float)i / 6 * rect.Width;
                string lb = (xMin + xRng * i / 6).ToString("F0");
                var sz = g.MeasureString(lb, font);
                g.DrawString(lb, font, brush, px - sz.Width / 2, rect.Bottom + 5);
            }
        }
    }
}