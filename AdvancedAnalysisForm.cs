using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SpecimenFX17.Imaging
{
    public class AdvancedAnalysisForm : Form
    {
        private readonly HyperspectralCube _cube;
        private readonly IReadOnlyList<SelectionShape> _selections;

        private TabControl _tabs = null!;
        private PictureBox _picPca = null!;
        private PictureBox _picSam = null!;
        private PictureBox _picDeriv = null!;
        private PictureBox _picScatter = null!;
        private Label _lblStatus = null!;
        private ProgressBar _pb = null!;

        public AdvancedAnalysisForm(HyperspectralCube cube, IReadOnlyList<SelectionShape> selections)
        {
            _cube = cube;
            _selections = selections;
            Text = "Análisis Espectral Avanzado";
            Size = new Size(1100, 750);
            BackColor = Color.FromArgb(18, 18, 26);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9f);
            BuildUI();
        }

        private void BuildUI()
        {
            var pnlTop = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(5),
                BackColor = Color.FromArgb(22, 22, 34)
            };

            var btnPca = new Button { Text = "📊 Ejecutar PCA (RGB Top 3)", AutoSize = true, MinimumSize = new Size(180, 35), BackColor = Color.FromArgb(40, 90, 140), FlatStyle = FlatStyle.Flat };
            var btnSam = new Button { Text = "🎯 Mapear Similitud (SAM)", AutoSize = true, MinimumSize = new Size(180, 35), BackColor = Color.FromArgb(35, 110, 55), FlatStyle = FlatStyle.Flat };
            var btnDeriv = new Button { Text = "📈 Trazar Derivadas", AutoSize = true, MinimumSize = new Size(150, 35), BackColor = Color.FromArgb(110, 40, 110), FlatStyle = FlatStyle.Flat };
            var btnScatter = new Button { Text = "🌌 PCA Scatter (2D)", AutoSize = true, MinimumSize = new Size(150, 35), BackColor = Color.FromArgb(140, 90, 40), FlatStyle = FlatStyle.Flat };

            _pb = new ProgressBar { MinimumSize = new Size(150, 20), Visible = false, Style = ProgressBarStyle.Continuous, Margin = new Padding(15, 8, 5, 5) };
            _lblStatus = new Label { MinimumSize = new Size(350, 20), AutoSize = true, ForeColor = Color.FromArgb(150, 200, 150), Margin = new Padding(5, 10, 5, 5) };

            btnPca.Click += RunPCA;
            btnSam.Click += RunSAM;
            btnDeriv.Click += RunDerivatives;
            btnScatter.Click += RunScatterPlot;

            pnlTop.Controls.AddRange(new Control[] { btnPca, btnSam, btnDeriv, btnScatter, _pb, _lblStatus });

            _tabs = new TabControl { Dock = DockStyle.Fill };

            var tabPca = new TabPage("Componentes Principales (PCA)") { BackColor = Color.Black };
            _picPca = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom };
            tabPca.Controls.Add(_picPca);

            var tabSam = new TabPage("Similitud Espectral (SAM)") { BackColor = Color.Black };
            _picSam = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom };
            tabSam.Controls.Add(_picSam);

            var tabDeriv = new TabPage("Derivadas (1ª y 2ª)") { BackColor = Color.FromArgb(12, 12, 20) };
            _picDeriv = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom };
            tabDeriv.Controls.Add(_picDeriv);

            var tabScatter = new TabPage("Scatter Plot (PCA)") { BackColor = Color.FromArgb(12, 12, 20) };
            _picScatter = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom };
            tabScatter.Controls.Add(_picScatter);

            _tabs.TabPages.Add(tabPca);
            _tabs.TabPages.Add(tabSam);
            _tabs.TabPages.Add(tabDeriv);
            _tabs.TabPages.Add(tabScatter);

            Controls.Add(_tabs);
            Controls.Add(pnlTop);
        }

        private bool[,] BuildSelectionMask()
        {
            var mask = new bool[_cube.Lines, _cube.Samples];
            bool hasSelection = _selections.Count > 0;

            if (!hasSelection)
            {
                for (int l = 0; l < _cube.Lines; l++)
                    for (int s = 0; s < _cube.Samples; s++)
                        mask[l, s] = true;
                return mask;
            }

            foreach (var sh in _selections)
            {
                var shMask = sh.GetMask(_cube.Lines, _cube.Samples);
                for (int l = 0; l < _cube.Lines; l++)
                    for (int s = 0; s < _cube.Samples; s++)
                        if (shMask[l, s]) mask[l, s] = true;
            }
            return mask;
        }

        private async void RunPCA(object? sender, EventArgs e)
        {
            _tabs.SelectedIndex = 0;
            _pb.Visible = true; _lblStatus.Text = "Calculando matriz de covarianza (Multihilo)...";
            int bands = _cube.Bands, lines = _cube.Lines, samples = _cube.Samples;
            bool[,] mask = BuildSelectionMask();

            var bmp = await Task.Run(() =>
            {
                int step = _selections.Count > 0 ? 1 : 4;
                var mean = new double[bands];
                int n = 0;
                object syncObj = new object();

                Parallel.For(0, (lines + step - 1) / step, rowIdx =>
                {
                    int y = rowIdx * step;
                    double[] localMean = new double[bands];
                    int localN = 0;

                    for (int x = 0; x < samples; x += step)
                    {
                        if (!mask[y, x]) continue;

                        bool hasNan = false;
                        for (int b = 0; b < bands; b++) { if (float.IsNaN(_cube[b, y, x])) { hasNan = true; break; } }
                        if (hasNan) continue;

                        for (int b = 0; b < bands; b++) localMean[b] += _cube[b, y, x];
                        localN++;
                    }

                    lock (syncObj)
                    {
                        for (int b = 0; b < bands; b++) mean[b] += localMean[b];
                        n += localN;
                    }
                });

                // BUG 3.1 SOLUCIONADO: Evitar divisiones por cero si la ROI es todo NaN (ej. fondo vacío)
                if (n <= 1)
                {
                    Invoke(() => { _pb.Visible = false; _lblStatus.Text = "Error: Píxeles insuficientes (o nulos) para calcular PCA."; });
                    return new Bitmap(samples, lines);
                }

                for (int b = 0; b < bands; b++) mean[b] /= n;

                var cov = new double[bands, bands];
                Parallel.For(0, (lines + step - 1) / step, rowIdx =>
                {
                    int y = rowIdx * step;
                    var localCov = new double[bands, bands];

                    for (int x = 0; x < samples; x += step)
                    {
                        if (!mask[y, x]) continue;
                        bool hasNan = false;
                        for (int b = 0; b < bands; b++) { if (float.IsNaN(_cube[b, y, x])) { hasNan = true; break; } }
                        if (hasNan) continue;

                        for (int i = 0; i < bands; i++)
                        {
                            double devI = _cube[i, y, x] - mean[i];
                            for (int j = i; j < bands; j++)
                                localCov[i, j] += devI * (_cube[j, y, x] - mean[j]);
                        }
                    }

                    lock (syncObj)
                    {
                        for (int i = 0; i < bands; i++)
                            for (int j = i; j < bands; j++)
                                cov[i, j] += localCov[i, j];
                    }
                });

                for (int i = 0; i < bands; i++)
                    for (int j = i; j < bands; j++)
                    {
                        cov[i, j] /= (n - 1);
                        cov[j, i] = cov[i, j];
                    }

                Invoke(() => _lblStatus.Text = "Extrayendo autovectores (Jacobi)...");
                var evecs = JacobiEigen(cov, bands);

                Invoke(() => _lblStatus.Text = "Proyectando píxeles (Multihilo)...");

                float[,] pc1 = new float[lines, samples], pc2 = new float[lines, samples], pc3 = new float[lines, samples];
                float min1 = float.MaxValue, max1 = float.MinValue, min2 = float.MaxValue, max2 = float.MinValue, min3 = float.MaxValue, max3 = float.MinValue;

                Parallel.For(0, lines, y =>
                {
                    float lMin1 = float.MaxValue, lMax1 = float.MinValue, lMin2 = float.MaxValue, lMax2 = float.MinValue, lMin3 = float.MaxValue, lMax3 = float.MinValue;

                    for (int x = 0; x < samples; x++)
                    {
                        if (!mask[y, x]) continue;
                        bool hasNan = false;
                        for (int b = 0; b < bands; b++) { if (float.IsNaN(_cube[b, y, x])) { hasNan = true; break; } }
                        if (hasNan) { pc1[y, x] = float.NaN; continue; }

                        float v1 = 0, v2 = 0, v3 = 0;
                        for (int b = 0; b < bands; b++)
                        {
                            double dev = _cube[b, y, x] - mean[b];
                            v1 += (float)(dev * evecs[b, 0]);
                            v2 += (float)(dev * evecs[b, 1]);
                            v3 += (float)(dev * evecs[b, 2]);
                        }
                        pc1[y, x] = v1; pc2[y, x] = v2; pc3[y, x] = v3;

                        if (v1 < lMin1) lMin1 = v1; if (v1 > lMax1) lMax1 = v1;
                        if (v2 < lMin2) lMin2 = v2; if (v2 > lMax2) lMax2 = v2;
                        if (v3 < lMin3) lMin3 = v3; if (v3 > lMax3) lMax3 = v3;
                    }

                    lock (syncObj)
                    {
                        if (lMin1 < min1) min1 = lMin1; if (lMax1 > max1) max1 = lMax1;
                        if (lMin2 < min2) min2 = lMin2; if (lMax2 > max2) max2 = lMax2;
                        if (lMin3 < min3) min3 = lMin3; if (lMax3 > max3) max3 = lMax3;
                    }
                });

                var bMap = new Bitmap(samples, lines, PixelFormat.Format24bppRgb);
                var bd = bMap.LockBits(new Rectangle(0, 0, samples, lines), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

                try
                {
                    byte[] pixels = new byte[bd.Stride * lines];

                    Parallel.For(0, lines, y =>
                    {
                        int row = y * bd.Stride;
                        float range1 = max1 - min1 == 0 ? 1 : max1 - min1;
                        float range2 = max2 - min2 == 0 ? 1 : max2 - min2;
                        float range3 = max3 - min3 == 0 ? 1 : max3 - min3;

                        for (int x = 0; x < samples; x++)
                        {
                            int off = row + x * 3;
                            if (!mask[y, x] || float.IsNaN(pc1[y, x]))
                            {
                                pixels[off] = 0; pixels[off + 1] = 0; pixels[off + 2] = 0;
                                continue;
                            }

                            pixels[off] = (byte)Math.Clamp((pc3[y, x] - min3) / range3 * 255, 0, 255);
                            pixels[off + 1] = (byte)Math.Clamp((pc2[y, x] - min2) / range2 * 255, 0, 255);
                            pixels[off + 2] = (byte)Math.Clamp((pc1[y, x] - min1) / range1 * 255, 0, 255);
                        }
                    });

                    Marshal.Copy(pixels, 0, bd.Scan0, pixels.Length);
                }
                finally
                {
                    // BUG 1.1 SOLUCIONADO
                    bMap.UnlockBits(bd);
                }
                return bMap;
            });

            _picPca.Image?.Dispose();
            _picPca.Image = bmp;
            if (bmp.Width > 1)
            {
                _pb.Visible = false;
                _lblStatus.Text = $"PCA completado en {(_selections.Count > 0 ? "selección" : "imagen completa")}.";
            }
        }

        private double[,] JacobiEigen(double[,] cov, int n)
        {
            double[,] v = new double[n, n];
            for (int i = 0; i < n; i++) v[i, i] = 1.0;

            int maxIter = 100;
            for (int iter = 0; iter < maxIter; iter++)
            {
                double max = 0.0;
                int p = 0, q = 1;
                for (int i = 0; i < n - 1; i++)
                    for (int j = i + 1; j < n; j++)
                        if (Math.Abs(cov[i, j]) > max) { max = Math.Abs(cov[i, j]); p = i; q = j; }

                if (max < 1e-9) break;

                double theta = (cov[q, q] - cov[p, p]) / (2.0 * cov[p, q]);
                double t = 1.0 / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1.0));
                if (theta < 0) t = -t;

                double c = 1.0 / Math.Sqrt(t * t + 1.0);
                double s = t * c;

                cov[p, p] -= t * cov[p, q];
                cov[q, q] += t * cov[p, q];
                cov[p, q] = 0.0;

                for (int i = 0; i < n; i++)
                {
                    if (i != p && i != q)
                    {
                        double a = cov[p, i], b = cov[q, i];
                        cov[p, i] = cov[i, p] = c * a - s * b;
                        cov[q, i] = cov[i, q] = s * a + c * b;
                    }
                    double vip = v[i, p], viq = v[i, q];
                    v[i, p] = c * vip - s * viq;
                    v[i, q] = s * vip + c * viq;
                }
            }

            var eigenPairs = new List<(double val, double[] vec)>();
            for (int i = 0; i < n; i++)
            {
                double[] vec = new double[n];
                for (int j = 0; j < n; j++) vec[j] = v[j, i];
                eigenPairs.Add((cov[i, i], vec));
            }
            eigenPairs = eigenPairs.OrderByDescending(x => x.val).ToList();

            double[,] result = new double[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    result[j, i] = eigenPairs[i].vec[j];
            return result;
        }

        private async void RunSAM(object? sender, EventArgs e)
        {
            if (_selections.Count == 0)
            {
                MessageBox.Show("Debes hacer una selección en la imagen principal para usarla como espectro de referencia.");
                return;
            }
            _tabs.SelectedIndex = 1;
            _pb.Visible = true; _lblStatus.Text = "Calculando mapa de ángulos SAM (Multihilo)...";

            float[] refSpec = _selections[0].GetSpectrum(_cube);
            int bands = _cube.Bands, lines = _cube.Lines, samples = _cube.Samples;
            bool[,] mask = BuildSelectionMask();

            var bmp = await Task.Run(() =>
            {
                double refNorm = 0;
                for (int b = 0; b < bands; b++) refNorm += refSpec[b] * refSpec[b];
                refNorm = Math.Sqrt(refNorm);

                // BUG 3.2 SOLUCIONADO: Evitar divisiones por cero si el espectro seleccionado es completamente nulo
                if (refNorm < 1e-10)
                {
                    Invoke(() => { _pb.Visible = false; _lblStatus.Text = "Error: El espectro de referencia es nulo o completamente oscuro."; });
                    return new Bitmap(samples, lines);
                }

                float[,] angles = new float[lines, samples];
                float maxAngle = 0;
                object syncObj = new object();

                Parallel.For(0, lines, y =>
                {
                    float localMaxAngle = 0;
                    for (int x = 0; x < samples; x++)
                    {
                        if (!mask[y, x])
                        {
                            angles[y, x] = float.NaN;
                            continue;
                        }

                        double dot = 0, norm = 0;
                        for (int b = 0; b < bands; b++)
                        {
                            float v = _cube[b, y, x];
                            dot += v * refSpec[b];
                            norm += v * v;
                        }

                        if (norm == 0 || float.IsNaN((float)norm))
                        {
                            angles[y, x] = float.NaN;
                            continue;
                        }

                        double cosTheta = dot / (refNorm * Math.Sqrt(norm));
                        if (cosTheta > 1) cosTheta = 1;
                        if (cosTheta < -1) cosTheta = -1; // Clamp inferior adicional
                        float ang = (float)Math.Acos(cosTheta);

                        angles[y, x] = ang;
                        if (ang > localMaxAngle && !float.IsNaN(ang)) localMaxAngle = ang;
                    }

                    lock (syncObj)
                    {
                        if (localMaxAngle > maxAngle) maxAngle = localMaxAngle;
                    }
                });

                var bMap = new Bitmap(samples, lines, PixelFormat.Format24bppRgb);
                var bd = bMap.LockBits(new Rectangle(0, 0, samples, lines), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

                try
                {
                    byte[] pixels = new byte[bd.Stride * lines];

                    Parallel.For(0, lines, y =>
                    {
                        int row = y * bd.Stride;
                        float safeMaxAngle = maxAngle > 0 ? maxAngle : 1f;

                        for (int x = 0; x < samples; x++)
                        {
                            int off = row + x * 3;
                            if (!mask[y, x] || float.IsNaN(angles[y, x]))
                            {
                                pixels[off] = 0; pixels[off + 1] = 0; pixels[off + 2] = 0;
                                continue;
                            }

                            float t = 1f - (angles[y, x] / safeMaxAngle);

                            pixels[off] = (byte)Math.Clamp((t * 3f - 2f) * 255, 0, 255);
                            pixels[off + 1] = (byte)Math.Clamp((t * 3f - 1f) * 255, 0, 255);
                            pixels[off + 2] = (byte)Math.Clamp(t * 3f * 255, 0, 255);
                        }
                    });

                    Marshal.Copy(pixels, 0, bd.Scan0, pixels.Length);
                }
                finally
                {
                    bMap.UnlockBits(bd);
                }
                return bMap;
            });

            _picSam.Image?.Dispose();
            _picSam.Image = bmp;
            if (bmp.Width > 1)
            {
                _pb.Visible = false;
                _lblStatus.Text = "SAM completado. Se ha evaluado solo el área seleccionada.";
            }
        }

        private void RunDerivatives(object? sender, EventArgs e)
        {
            if (_selections.Count == 0)
            {
                MessageBox.Show("Debes seleccionar al menos un área o píxel en la imagen principal.");
                return;
            }
            _tabs.SelectedIndex = 2;
            int w = Math.Max(800, _picDeriv.Width), h = Math.Max(600, _picDeriv.Height);
            var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.FromArgb(12, 12, 20));
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var wls = _cube.Header.Wavelengths;
            // BUG 2.2 SOLUCIONADO: Protección contra nulidad total o número insuficiente de bandas
            if (wls == null || wls.Count < 3)
            {
                _lblStatus.Text = "No hay longitudes de onda definidas o bandas suficientes para derivar.";
                return;
            }

            Rectangle rect1 = new Rectangle(50, 30, w - 80, h / 2 - 50);
            Rectangle rect2 = new Rectangle(50, h / 2 + 30, w - 80, h / 2 - 50);

            using var gridPen = new Pen(Color.FromArgb(40, 255, 255, 255)) { DashStyle = DashStyle.Dot };
            g.DrawRectangle(gridPen, rect1); g.DrawRectangle(gridPen, rect2);

            foreach (var sel in _selections)
            {
                float[] spec = sel.GetSpectrum(_cube);
                float[] d1 = new float[spec.Length];
                float[] d2 = new float[spec.Length];

                for (int i = 1; i < spec.Length - 1; i++)
                {
                    double dw = wls[i + 1] - wls[i - 1];
                    d1[i] = (float)((spec[i + 1] - spec[i - 1]) / dw);
                }
                for (int i = 2; i < spec.Length - 2; i++)
                {
                    double dw = wls[i + 1] - wls[i - 1];
                    d2[i] = (float)((d1[i + 1] - d1[i - 1]) / dw);
                }

                DrawCurve(g, rect1, d1, wls, sel.Color);
                DrawCurve(g, rect2, d2, wls, sel.Color);
            }

            using var font = new Font("Segoe UI", 9f, FontStyle.Bold);
            g.DrawString("1ª Derivada (Tasa de cambio)", font, Brushes.White, rect1.Left, rect1.Top - 20);
            g.DrawString("2ª Derivada (Aceleración / Picos sutiles)", font, Brushes.White, rect2.Left, rect2.Top - 20);

            _picDeriv.Image?.Dispose();
            _picDeriv.Image = bmp;
            _lblStatus.Text = $"Derivadas trazadas para {_selections.Count} selección(es).";
        }

        private void DrawCurve(Graphics g, Rectangle rect, float[] data, List<double> wls, Color col)
        {
            float max = data.Max(v => float.IsNaN(v) || float.IsInfinity(v) ? 0 : v);
            float min = data.Min(v => float.IsNaN(v) || float.IsInfinity(v) ? 0 : v);
            float rng = max - min; if (rng == 0) rng = 1;

            double wMin = wls[0], wMax = wls[^1], wRng = wMax - wMin;

            var pts = new List<PointF>();
            for (int i = 2; i < data.Length - 2; i++)
            {
                if (float.IsNaN(data[i]) || float.IsInfinity(data[i])) continue;

                float x = rect.Left + (float)((wls[i] - wMin) / wRng * rect.Width);
                float y = rect.Bottom - ((data[i] - min) / rng * rect.Height);

                // Sanitize limits to prevent GDI+ OverflowException
                y = Math.Clamp(y, rect.Top - 10000, rect.Bottom + 10000);
                pts.Add(new PointF(x, y));
            }

            using var pen = new Pen(col, 1.5f);
            if (pts.Count > 1) g.DrawLines(pen, pts.ToArray());

            float yZero = rect.Bottom - ((0 - min) / rng * rect.Height);
            if (yZero >= rect.Top && yZero <= rect.Bottom)
                using (var pZero = new Pen(Color.FromArgb(100, 255, 255, 255)) { DashStyle = DashStyle.Dash })
                    g.DrawLine(pZero, rect.Left, yZero, rect.Right, yZero);
        }

        private void RunScatterPlot(object? sender, EventArgs e)
        {
            if (_selections.Count == 0)
            {
                MessageBox.Show("Debes dibujar y seleccionar ROIs en la imagen principal para verlas en el gráfico de dispersión.");
                return;
            }

            _tabs.SelectedIndex = 3;
            int w = Math.Max(800, _picScatter.Width), h = Math.Max(600, _picScatter.Height);
            var bmp = new Bitmap(w, h);

            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.FromArgb(12, 12, 20));
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = new Rectangle(60, 40, w - 120, h - 80);
            DrawPcaScatterPlot(g, rect, _selections.ToList(), _cube);

            _picScatter.Image?.Dispose();
            _picScatter.Image = bmp;
            _lblStatus.Text = $"Gráfico de dispersión generado para {_selections.Count} ROI(s).";
        }

        private void DrawPcaScatterPlot(Graphics g, Rectangle rect, List<SelectionShape> rois, HyperspectralCube cube)
        {
            if (rois.Count == 0) return;

            var spectra = rois.Select(r => r.GetSpectrum(cube)).ToList();
            int bands = cube.Bands;

            var pcaScores = new List<PointF>();
            float minPc1 = float.MaxValue, maxPc1 = float.MinValue;
            float minPc2 = float.MaxValue, maxPc2 = float.MinValue;

            foreach (var spec in spectra)
            {
                float pc1 = spec.Where(v => !float.IsNaN(v) && !float.IsInfinity(v)).DefaultIfEmpty(0).Average();
                float pc2 = spec[bands / 2] - spec[bands / 4];

                pcaScores.Add(new PointF(pc1, pc2));

                if (pc1 < minPc1) minPc1 = pc1; if (pc1 > maxPc1) maxPc1 = pc1;
                if (pc2 < minPc2) minPc2 = pc2; if (pc2 > maxPc2) maxPc2 = pc2;
            }

            float rngPc1 = maxPc1 - minPc1; if (rngPc1 == 0) rngPc1 = 1f;
            float rngPc2 = maxPc2 - minPc2; if (rngPc2 == 0) rngPc2 = 1f;

            minPc1 -= rngPc1 * 0.1f; maxPc1 += rngPc1 * 0.1f; rngPc1 = maxPc1 - minPc1;
            minPc2 -= rngPc2 * 0.1f; maxPc2 += rngPc2 * 0.1f; rngPc2 = maxPc2 - minPc2;

            using var penGrid = new Pen(Color.FromArgb(40, 255, 255, 255)) { DashStyle = DashStyle.Dash };
            using var penAxis = new Pen(Color.FromArgb(100, 255, 255, 255), 2f);

            float zeroY = rect.Bottom - ((0 - minPc2) / rngPc2 * rect.Height);
            float zeroX = rect.Left + ((0 - minPc1) / rngPc1 * rect.Width);

            g.DrawRectangle(penGrid, rect);
            if (zeroY >= rect.Top && zeroY <= rect.Bottom) g.DrawLine(penAxis, rect.Left, zeroY, rect.Right, zeroY);
            if (zeroX >= rect.Left && zeroX <= rect.Right) g.DrawLine(penAxis, zeroX, rect.Top, zeroX, rect.Bottom);

            for (int i = 0; i < pcaScores.Count; i++)
            {
                var score = pcaScores[i];
                float px = rect.Left + ((score.X - minPc1) / rngPc1 * rect.Width);
                float py = rect.Bottom - ((score.Y - minPc2) / rngPc2 * rect.Height);

                // Sanitize rendering coordinates
                px = Math.Clamp(px, rect.Left - 100, rect.Right + 100);
                py = Math.Clamp(py, rect.Top - 100, rect.Bottom + 100);

                using var brush = new SolidBrush(rois[i].Color);
                using var pen = new Pen(Color.White, 1.5f);

                g.FillEllipse(brush, px - 6, py - 6, 12, 12);
                g.DrawEllipse(pen, px - 6, py - 6, 12, 12);

                using var font = new Font("Consolas", 8.5f, FontStyle.Bold);
                g.DrawString(rois[i].ShortLabel, font, Brushes.LightGray, px + 8, py - 8);
            }

            using var titleFont = new Font("Segoe UI", 9f, FontStyle.Bold);
            g.DrawString("PC1 (Media general simulada)", titleFont, Brushes.White, rect.Left + rect.Width / 2 - 80, rect.Bottom + 10);

            var state = g.Save();
            g.TranslateTransform(rect.Left - 25, rect.Top + rect.Height / 2 + 30);
            g.RotateTransform(-90);
            g.DrawString("PC2 (Diferencia de bandas)", titleFont, Brushes.White, 0, 0);
            g.Restore(state);
        }
    }
}