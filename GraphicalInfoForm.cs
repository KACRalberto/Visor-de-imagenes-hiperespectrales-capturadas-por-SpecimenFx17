using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace SpecimenFX17.Imaging
{
    public class GraphicalInfoForm : Form
    {
        private HyperspectralCube _cube;
        private PictureBox _picStats;
        private PictureBox _picHisto;
        private PictureBox _picPixel;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int CurrentBand { get; set; } = 0;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Point CurrentPixel { get; set; } = new Point(0, 0);

        public GraphicalInfoForm(HyperspectralCube cube)
        {
            _cube = cube;
            Text = "Información Gráfica (Estilo Hyper)";
            Size = new Size(400, 800);
            BackColor = Color.FromArgb(240, 240, 240);
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            TopMost = true;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 1
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));

            _picStats = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(5) };
            _picHisto = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(5) };
            _picPixel = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(5) };

            _picStats.Paint += PaintStats;
            _picHisto.Paint += PaintHisto;
            _picPixel.Paint += PaintPixel;

            layout.Controls.Add(_picStats, 0, 0);
            layout.Controls.Add(_picHisto, 0, 1);
            layout.Controls.Add(_picPixel, 0, 2);

            Controls.Add(layout);
        }

        public void UpdateData(int band, Point pixel)
        {
            CurrentBand = band;
            CurrentPixel = pixel;
            _picStats.Invalidate();
            _picHisto.Invalidate();
            _picPixel.Invalidate();
        }

        private void PaintStats(object? sender, PaintEventArgs e)
        {
            if (_cube.BandStats == null || _cube.BandStats.Count == 0) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int w = _picStats.Width, h = _picStats.Height;
            float margin = 10f;
            int bands = _cube.Header.Bands;

            float maxVal = _cube.BandStats.Max(s => s.Max);
            float minVal = _cube.BandStats.Min(s => s.Min);
            float range = maxVal - minVal; if (range == 0) range = 1;

            float fx = (w - margin * 2) / (bands - 1);
            float fy = (h - margin * 2) / range;

            PointF[] pMin = new PointF[bands], pMax = new PointF[bands];
            PointF[] pMean = new PointF[bands], pStdPos = new PointF[bands], pStdNeg = new PointF[bands];

            for (int i = 0; i < bands; i++)
            {
                var st = _cube.BandStats[i];
                float x = margin + i * fx;
                pMin[i] = new PointF(x, h - margin - (st.Min - minVal) * fy);
                pMax[i] = new PointF(x, h - margin - (st.Max - minVal) * fy);
                pMean[i] = new PointF(x, h - margin - (st.Mean - minVal) * fy);
                pStdPos[i] = new PointF(x, h - margin - ((st.Mean + st.Std) - minVal) * fy);
                pStdNeg[i] = new PointF(x, h - margin - ((st.Mean - st.Std) - minVal) * fy);
            }

            g.DrawLines(Pens.Blue, pMin);
            g.DrawLines(Pens.Red, pMax);
            g.DrawLines(Pens.Green, pMean);
            g.DrawLines(Pens.Orange, pStdPos);
            g.DrawLines(Pens.Cyan, pStdNeg);

            g.DrawString("Estadísticos ROI (Min, Max, Media, ±Std)", new Font("Arial", 8), Brushes.Black, margin, margin);
        }

        private void PaintHisto(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            int w = _picHisto.Width, h = _picHisto.Height;
            float margin = 10f;

            int safeBand = Math.Clamp(CurrentBand, 0, _cube.Bands - 1);
            var data = _cube.GetBand(safeBand);

            int bins = 256;
            int[] counts = new int[bins];
            float min = float.MaxValue, max = float.MinValue;

            for (int l = 0; l < _cube.Lines; l++)
                for (int s = 0; s < _cube.Samples; s++)
                {
                    float v = data[l, s];
                    if (!float.IsNaN(v)) { if (v < min) min = v; if (v > max) max = v; }
                }

            float range = max - min; if (range == 0) range = 1;

            for (int l = 0; l < _cube.Lines; l++)
                for (int s = 0; s < _cube.Samples; s++)
                {
                    float v = data[l, s];
                    if (!float.IsNaN(v))
                    {
                        int b = (int)((v - min) / range * (bins - 1));
                        counts[b]++;
                    }
                }

            int maxCount = counts.Max();
            float binW = (w - margin * 2) / bins;
            float fy = (h - margin * 2) / (maxCount == 0 ? 1 : maxCount);

            for (int i = 0; i < bins; i++)
            {
                float barH = counts[i] * fy;
                g.FillRectangle(Brushes.DarkGray, margin + i * binW, h - margin - barH, Math.Max(1, binW), barH);
            }
            g.DrawString($"Histograma Banda {safeBand + 1}", new Font("Arial", 8), Brushes.Black, margin, margin);
        }

        private void PaintPixel(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int w = _picPixel.Width, h = _picPixel.Height;
            float margin = 10f;

            if (CurrentPixel.X < 0 || CurrentPixel.X >= _cube.Samples || CurrentPixel.Y < 0 || CurrentPixel.Y >= _cube.Lines) return;

            int bands = _cube.Header.Bands;
            float[] spec = new float[bands];
            float min = float.MaxValue, max = float.MinValue;

            for (int i = 0; i < bands; i++)
            {
                spec[i] = _cube[i, CurrentPixel.Y, CurrentPixel.X];
                if (!float.IsNaN(spec[i])) { if (spec[i] < min) min = spec[i]; if (spec[i] > max) max = spec[i]; }
            }

            float range = max - min; if (range == 0) range = 1;
            float fx = (w - margin * 2) / (bands - 1);
            float fy = (h - margin * 2) / range;

            PointF[] pts = new PointF[bands];
            for (int i = 0; i < bands; i++)
            {
                pts[i] = new PointF(margin + i * fx, h - margin - (spec[i] - min) * fy);
            }

            g.DrawLine(Pens.LightGray, margin, h / 2, w - margin, h / 2);
            if (pts.Length > 1) g.DrawLines(Pens.Black, pts);

            g.DrawString($"Píxel ({CurrentPixel.X}, {CurrentPixel.Y})", new Font("Arial", 8), Brushes.Black, margin, margin);

            if (CurrentBand < bands)
            {
                float bx = margin + CurrentBand * fx;
                using var pen = new Pen(Color.Red, 1f) { DashStyle = DashStyle.Dash };
                g.DrawLine(pen, bx, margin, bx, h - margin);
            }
        }
    }
}