using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SpecimenFX17.Imaging
{
    public class DualViewerForm : Form
    {
        private HyperspectralCube? _cube1;
        private HyperspectralCube? _cube2;

        private PictureBox _pic1 = new();
        private PictureBox _pic2 = new();
        private PictureBox _plot = new();
        private Button _btnLoad1 = new();
        private Button _btnLoad2 = new();

        private Point? _pt1;
        private Point? _pt2;

        public DualViewerForm(HyperspectralCube? baseCube)
        {
            _cube1 = baseCube;
            Text = "Comparador Multifichero de Firmas Espectrales";
            Size = new Size(1200, 800);
            BackColor = Color.FromArgb(18, 18, 26);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9f);

            BuildUI();
            if (_cube1 != null) RenderImage(1);
        }

        private void BuildUI()
        {
            var pnlTop = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(5),
                BackColor = Color.FromArgb(22, 22, 34)
            };

            _btnLoad1 = new Button { Text = "📂 Cargar Cubo Izquierdo", AutoSize = true, MinimumSize = new Size(200, 35), BackColor = Color.FromArgb(50, 90, 140), FlatStyle = FlatStyle.Flat, ForeColor = Color.White };
            _btnLoad2 = new Button { Text = "📂 Cargar Cubo Derecho", AutoSize = true, MinimumSize = new Size(200, 35), BackColor = Color.FromArgb(140, 90, 50), FlatStyle = FlatStyle.Flat, ForeColor = Color.White };

            _btnLoad1.Click += (s, e) => LoadCube(1);
            _btnLoad2.Click += (s, e) => LoadCube(2);

            pnlTop.Controls.Add(_btnLoad1);
            pnlTop.Controls.Add(_btnLoad2);

            var splitImages = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 4 };
            _pic1 = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black, Cursor = Cursors.Cross };
            _pic2 = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black, Cursor = Cursors.Cross };

            _pic1.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { _pt1 = MapToImage(_pic1, e.Location); _pic1.Invalidate(); DrawPlot(); } };
            _pic2.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { _pt2 = MapToImage(_pic2, e.Location); _pic2.Invalidate(); DrawPlot(); } };

            _pic1.Paint += (s, e) => DrawCrosshair(e.Graphics, _pic1, _pt1, Color.Cyan);
            _pic2.Paint += (s, e) => DrawCrosshair(e.Graphics, _pic2, _pt2, Color.Orange);

            splitImages.Panel1.Controls.Add(_pic1);
            splitImages.Panel2.Controls.Add(_pic2);

            var splitMain = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 400, SplitterWidth = 4 };
            splitMain.Panel1.Controls.Add(splitImages);

            _plot = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(12, 12, 20) };
            _plot.Resize += (s, e) => DrawPlot();
            splitMain.Panel2.Controls.Add(_plot);

            Controls.Add(splitMain);
            Controls.Add(pnlTop);
        }

        private async void LoadCube(int target)
        {
            using var dlg = new OpenFileDialog { Title = "Abrir imagen hiperespectral ENVI", Filter = "ENVI Header (*.hdr)|*.hdr|Todos|*.*" };
            if (dlg.ShowDialog() != DialogResult.OK) return;

            var progress = new Progress<int>();
            var cube = await Task.Run(() => HyperspectralCube.Load(dlg.FileName, progress));

            if (target == 1) { _cube1 = cube; _pt1 = null; RenderImage(1); }
            else { _cube2 = cube; _pt2 = null; RenderImage(2); }
            DrawPlot();
        }

        private void RenderImage(int target)
        {
            var cube = target == 1 ? _cube1 : _cube2;
            if (cube == null) return;

            var opts = new BliRenderOptions
            {
                Colormap = BliColormap.Grayscale,
                DrawColorbar = false
            };

            var bmp = BliRenderer.RenderBand(cube, cube.Bands / 2, opts);

            if (target == 1) { _pic1.Image?.Dispose(); _pic1.Image = bmp; _pic1.Invalidate(); }
            else { _pic2.Image?.Dispose(); _pic2.Image = bmp; _pic2.Invalidate(); }
        }

        private Point? MapToImage(PictureBox pb, Point sc)
        {
            if (pb.Image == null) return null;
            float scale = Math.Min((float)pb.Width / pb.Image.Width, (float)pb.Height / pb.Image.Height);
            float ox = (pb.Width - (pb.Image.Width * scale)) / 2f;
            float oy = (pb.Height - (pb.Image.Height * scale)) / 2f;
            int x = (int)((sc.X - ox) / scale);
            int y = (int)((sc.Y - oy) / scale);
            if (x < 0 || x >= pb.Image.Width || y < 0 || y >= pb.Image.Height) return null;
            return new Point(x, y);
        }

        private void DrawCrosshair(Graphics g, PictureBox pb, Point? ptImg, Color color)
        {
            if (pb.Image == null || !ptImg.HasValue) return;
            float scale = Math.Min((float)pb.Width / pb.Image.Width, (float)pb.Height / pb.Image.Height);
            float ox = (pb.Width - (pb.Image.Width * scale)) / 2f;
            float oy = (pb.Height - (pb.Image.Height * scale)) / 2f;
            float scX = ox + ptImg.Value.X * scale;
            float scY = oy + ptImg.Value.Y * scale;

            using var pen = new Pen(color, 2f);
            using var bgPen = new Pen(Color.FromArgb(150, 0, 0, 0), 4f);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int r = 8;
            g.DrawEllipse(bgPen, scX - r, scY - r, r * 2, r * 2);
            g.DrawEllipse(pen, scX - r, scY - r, r * 2, r * 2);
            g.DrawLine(bgPen, scX, scY - r - 4, scX, scY + r + 4);
            g.DrawLine(bgPen, scX - r - 4, scY, scX + r + 4, scY);
            g.DrawLine(pen, scX, scY - r - 4, scX, scY + r + 4);
            g.DrawLine(pen, scX - r - 4, scY, scX + r + 4, scY);
        }

        private void DrawPlot()
        {
            if (_plot.Width < 50 || _plot.Height < 50) return;
            var bmp = new Bitmap(_plot.Width, _plot.Height);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(Color.FromArgb(12, 12, 20));

            var rect = new Rectangle(70, 20, bmp.Width - 100, bmp.Height - 60);

            using (var gp = new Pen(Color.FromArgb(28, 255, 255, 255), 1f) { DashStyle = DashStyle.Dot })
            {
                for (int i = 0; i <= 5; i++) g.DrawLine(gp, rect.Left, rect.Bottom - (float)i / 5 * rect.Height, rect.Right, rect.Bottom - (float)i / 5 * rect.Height);
                for (int i = 0; i <= 6; i++) g.DrawLine(gp, rect.Left + (float)i / 6 * rect.Width, rect.Top, rect.Left + (float)i / 6 * rect.Width, rect.Bottom);
            }
            using (var bp = new Pen(Color.FromArgb(60, 255, 255, 255))) g.DrawRectangle(bp, rect);

            float[]? s1 = null, s2 = null;
            float yMin = float.MaxValue, yMax = float.MinValue;

            if (_cube1 != null && _pt1.HasValue)
            {
                s1 = _cube1.GetSpectrum(_pt1.Value.Y, _pt1.Value.X);
                foreach (var v in s1) { if (!float.IsNaN(v) && !float.IsInfinity(v)) { if (v < yMin) yMin = v; if (v > yMax) yMax = v; } }
            }
            if (_cube2 != null && _pt2.HasValue)
            {
                s2 = _cube2.GetSpectrum(_pt2.Value.Y, _pt2.Value.X);
                foreach (var v in s2) { if (!float.IsNaN(v) && !float.IsInfinity(v)) { if (v < yMin) yMin = v; if (v > yMax) yMax = v; } }
            }

            if (yMin == float.MaxValue) { yMin = 0; yMax = 1; }
            if (yMin == yMax) { yMin -= 0.5f; yMax += 0.5f; } // Salvaguarda si la línea es plana

            float yRng = yMax - yMin;
            if (yRng < 1e-10f) yRng = 1f;
            yMin -= yRng * 0.05f;
            yMax += yRng * 0.05f;
            yRng = yMax - yMin;

            if (s1 != null && _cube1 != null) DrawLine(g, rect, s1, _cube1.Header.Wavelengths, yMin, yRng, Color.Cyan, $"Izquierda ({_pt1!.Value.X}, {_pt1!.Value.Y})", 0);
            if (s2 != null && _cube2 != null) DrawLine(g, rect, s2, _cube2.Header.Wavelengths, yMin, yRng, Color.Orange, $"Derecha ({_pt2!.Value.X}, {_pt2!.Value.Y})", 1);

            using var font = new Font("Consolas", 8f);
            using var brush = new SolidBrush(Color.FromArgb(180, 180, 200));

            for (int i = 0; i <= 5; i++)
            {
                float py = rect.Bottom - (float)i / 5 * rect.Height;
                float val = yMin + yRng * i / 5;
                string lb = Math.Abs(val) >= 10000 || (Math.Abs(val) < 0.001f && val != 0) ? val.ToString("0.0e0") : val.ToString("G4");
                var sz = g.MeasureString(lb, font);
                g.DrawString(lb, font, brush, rect.Left - sz.Width - 5, py - sz.Height / 2);
            }

            double wMin = 0, wMax = s1?.Length ?? s2?.Length ?? 100;
            if (_cube1 != null && _cube1.Header.Wavelengths.Count > 0) { wMin = _cube1.Header.Wavelengths[0]; wMax = _cube1.Header.Wavelengths[^1]; }
            else if (_cube2 != null && _cube2.Header.Wavelengths.Count > 0) { wMin = _cube2.Header.Wavelengths[0]; wMax = _cube2.Header.Wavelengths[^1]; }

            for (int i = 0; i <= 6; i++)
            {
                float px = rect.Left + (float)i / 6 * rect.Width;
                string lb = (wMin + (wMax - wMin) * i / 6).ToString("F0");
                var sz = g.MeasureString(lb, font);
                g.DrawString(lb, font, brush, px - sz.Width / 2, rect.Bottom + 5);
            }

            _plot.Image?.Dispose();
            _plot.Image = bmp;
            _plot.Invalidate();
        }

        private void DrawLine(Graphics g, Rectangle rect, float[] spec, List<double> wls, float yMin, float yRng, Color col, string legend, int legendLine)
        {
            if (spec.Length < 2) return;

            // Salvaguarda para asegurar que siempre haya coordenadas X
            if (wls == null || wls.Count < spec.Length)
                wls = Enumerable.Range(0, spec.Length).Select(i => (double)i).ToList();

            double xMin = wls[0], xMax = wls[^1], xRng = xMax - xMin;
            if (xRng == 0) xRng = 1;

            var pts = new List<PointF>();
            for (int i = 0; i < spec.Length; i++)
            {
                if (float.IsNaN(spec[i]) || float.IsInfinity(spec[i])) continue;

                float px = rect.Left + (float)((wls[i] - xMin) / xRng * rect.Width);
                float py = rect.Bottom - ((spec[i] - yMin) / yRng * rect.Height);

                // Evitar crashes severos de GDI+ limitando coordenadas excesivas
                py = Math.Clamp(py, -10000f, 10000f);
                pts.Add(new PointF(px, py));
            }

            if (pts.Count > 1)
            {
                using var pen = new Pen(col, 2f) { LineJoin = LineJoin.Round };
                g.DrawLines(pen, pts.ToArray());
            }

            using var fontLegend = new Font("Segoe UI", 9f, FontStyle.Bold);
            using var brushLegend = new SolidBrush(col);
            g.DrawString(legend, fontLegend, brushLegend, rect.Left + 15, rect.Top + 10 + (legendLine * 20));
        }
    }
}