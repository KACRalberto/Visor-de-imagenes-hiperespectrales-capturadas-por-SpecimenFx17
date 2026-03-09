using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SpecimenFX17.Imaging
{
    public class PlsPredictionForm : Form
    {
        private readonly HyperspectralCube _cube;
        private readonly IReadOnlyList<SelectionShape> _selections;

        private PictureBox _picBrixMap = null!;
        private Label _lblStatus = null!;
        private ProgressBar _pb = null!;
        private double _plsIntercept = 0;
        private double[]? _plsCoefs = null;

        public PlsPredictionForm(HyperspectralCube cube, IReadOnlyList<SelectionShape> selections)
        {
            _cube = cube; _selections = selections;
            Text = "Predicción PLS de °Brix";
            Size = new Size(1000, 700); BackColor = Color.FromArgb(18, 18, 26); ForeColor = Color.White;
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

            var btnExport = new Button { Text = "📥 1. Exportar Espectros (CSV)", AutoSize = true, MinimumSize = new Size(200, 35), BackColor = Color.FromArgb(40, 90, 140), FlatStyle = FlatStyle.Flat };
            var btnLoadModel = new Button { Text = "📂 2. Cargar Modelo PLS", AutoSize = true, MinimumSize = new Size(180, 35), BackColor = Color.FromArgb(110, 40, 110), FlatStyle = FlatStyle.Flat };
            var btnPredict = new Button { Text = "🔥 3. Generar Mapa °Brix", AutoSize = true, MinimumSize = new Size(180, 35), BackColor = Color.FromArgb(35, 110, 55), FlatStyle = FlatStyle.Flat, Enabled = false };

            _pb = new ProgressBar { MinimumSize = new Size(120, 20), Visible = false, Style = ProgressBarStyle.Continuous, Margin = new Padding(15, 8, 5, 5) };
            _lblStatus = new Label { MinimumSize = new Size(250, 20), AutoSize = true, ForeColor = Color.FromArgb(150, 200, 150), Margin = new Padding(5, 10, 5, 5) };

            btnExport.Click += ExportSpectraToCsv;
            btnLoadModel.Click += (s, e) => { LoadPlsModel(); btnPredict.Enabled = _plsCoefs != null; };
            btnPredict.Click += GenerateBrixMap;

            pnlTop.Controls.AddRange(new Control[] { btnExport, btnLoadModel, btnPredict, _pb, _lblStatus });
            _picBrixMap = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
            Controls.Add(_picBrixMap); Controls.Add(pnlTop);
        }

        private bool[,] BuildSelectionMask()
        {
            var mask = new bool[_cube.Lines, _cube.Samples];
            if (_selections.Count == 0)
            {
                for (int l = 0; l < _cube.Lines; l++)
                    for (int s = 0; s < _cube.Samples; s++) mask[l, s] = true;
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

        private async void ExportSpectraToCsv(object? sender, EventArgs e)
        {
            using var dlg = new SaveFileDialog { Filter = "CSV UTF-8 (*.csv)|*.csv", FileName = "espectros_naranjas.csv" };
            if (dlg.ShowDialog() != DialogResult.OK) return;

            _pb.Visible = true; _lblStatus.Text = "Exportando datos (Multihilo)...";
            bool[,] mask = BuildSelectionMask();
            int lines = _cube.Lines, samples = _cube.Samples, bands = _cube.Bands;

            await Task.Run(() =>
            {
                string[][] rows = new string[lines][];

                Parallel.For(0, lines, y =>
                {
                    rows[y] = new string[samples];
                    for (int x = 0; x < samples; x++)
                    {
                        if (!mask[y, x]) continue;
                        var sb = new StringBuilder($"{x},{y},");
                        for (int b = 0; b < bands; b++)
                        {
                            sb.Append(FormatFloat(_cube[b, y, x]));
                            if (b < bands - 1) sb.Append(",");
                        }
                        rows[y][x] = sb.ToString();
                    }
                });

                using var writer = new StreamWriter(dlg.FileName, false, Encoding.UTF8);
                var header = new StringBuilder("PixelX,PixelY,");
                for (int b = 0; b < bands; b++)
                    header.Append($"Wl_{_cube.Header.Wavelengths[b].ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}{(b == bands - 1 ? "" : ",")}");
                writer.WriteLine(header.ToString());

                for (int y = 0; y < lines; y++)
                    for (int x = 0; x < samples; x++)
                        if (rows[y][x] != null) writer.WriteLine(rows[y][x]);
            });

            _pb.Visible = false; _lblStatus.Text = "Exportación CSV completada.";
        }

        private string FormatFloat(float v) => float.IsNaN(v) ? "0" : v.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private void LoadPlsModel()
        {
            using var dlg = new OpenFileDialog { Filter = "Modelo PLS CSV (*.csv)|*.csv", Title = "Selecciona el archivo de coeficientes PLS" };
            if (dlg.ShowDialog() != DialogResult.OK) return;

            try
            {
                var lines = File.ReadAllLines(dlg.FileName);
                _plsIntercept = double.Parse(lines[0].Split(',')[1], System.Globalization.CultureInfo.InvariantCulture);

                var coefStrings = lines[1].Split(',').Skip(1).ToArray();
                if (coefStrings.Length != _cube.Bands)
                {
                    MessageBox.Show($"El modelo tiene {coefStrings.Length} bandas, pero el cubo tiene {_cube.Bands}.", "Error");
                    return;
                }
                _plsCoefs = coefStrings.Select(s => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                _lblStatus.Text = $"Modelo PLS cargado ({_cube.Bands} bandas).";
            }
            catch (Exception ex) { MessageBox.Show($"Error: {ex.Message}"); }
        }

        private async void GenerateBrixMap(object? sender, EventArgs e)
        {
            if (_plsCoefs == null) return;
            _pb.Visible = true; _lblStatus.Text = "Calculando predicción PLS (Multihilo)...";
            bool[,] mask = BuildSelectionMask();
            int lines = _cube.Lines, samples = _cube.Samples, bands = _cube.Bands;

            var bmp = await Task.Run(() =>
            {
                float[,] brixMap = new float[lines, samples];
                float minBrix = float.MaxValue, maxBrix = float.MinValue;
                object syncObj = new object();

                Parallel.For(0, lines, y =>
                {
                    float lMin = float.MaxValue, lMax = float.MinValue;
                    for (int x = 0; x < samples; x++)
                    {
                        if (!mask[y, x]) { brixMap[y, x] = float.NaN; continue; }
                        double pred = _plsIntercept;
                        for (int b = 0; b < bands; b++) pred += _cube[b, y, x] * _plsCoefs[b];
                        float val = (float)pred;
                        brixMap[y, x] = val;
                        if (val < lMin) lMin = val;
                        if (val > lMax) lMax = val;
                    }
                    lock (syncObj)
                    {
                        if (lMin < minBrix) minBrix = lMin;
                        if (lMax > maxBrix) maxBrix = lMax;
                    }
                });

                var bMap = new Bitmap(samples, lines, PixelFormat.Format24bppRgb);
                var bd = bMap.LockBits(new Rectangle(0, 0, samples, lines), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
                byte[] pixels = new byte[bd.Stride * lines];
                float range = maxBrix - minBrix == 0 ? 1 : maxBrix - minBrix;

                Parallel.For(0, lines, y =>
                {
                    int row = y * bd.Stride;
                    for (int x = 0; x < samples; x++)
                    {
                        int off = row + x * 3;
                        if (float.IsNaN(brixMap[y, x]))
                        {
                            pixels[off] = 0; pixels[off + 1] = 0; pixels[off + 2] = 0;
                            continue;
                        }
                        float t = (brixMap[y, x] - minBrix) / range;
                        var (r, g, b) = GetHeatMapColor(t);
                        pixels[off] = b; pixels[off + 1] = g; pixels[off + 2] = r;
                    }
                });

                Marshal.Copy(pixels, 0, bd.Scan0, pixels.Length);
                bMap.UnlockBits(bd);

                using var gfx = Graphics.FromImage(bMap);
                DrawLegend(gfx, minBrix, maxBrix, bMap.Width, bMap.Height);
                return bMap;
            });

            _picBrixMap.Image?.Dispose(); _picBrixMap.Image = bmp;
            _pb.Visible = false; _lblStatus.Text = "Mapa °Brix generado con éxito.";
        }

        private (byte R, byte G, byte B) GetHeatMapColor(float t)
        {
            float r = Math.Clamp(1.5f - Math.Abs(2f * t - 1.5f), 0, 1);
            float g = Math.Clamp(1.5f - Math.Abs(2f * t - 1.0f), 0, 1);
            float b = Math.Clamp(1.5f - Math.Abs(2f * t - 0.5f), 0, 1);
            return ((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }

        private void DrawLegend(Graphics g, float min, float max, int w, int h)
        {
            int barW = 20, barH = 150;
            int x0 = w - barW - 15, y0 = 20;

            for (int i = 0; i < barH; i++)
            {
                float t = 1f - (float)i / barH;
                var (r, gc, b) = GetHeatMapColor(t);
                using var pen = new Pen(Color.FromArgb(r, gc, b));
                g.DrawLine(pen, x0, y0 + i, x0 + barW, y0 + i);
            }
            g.DrawRectangle(Pens.White, x0, y0, barW, barH);

            using var font = new Font("Arial", 9, FontStyle.Bold);
            g.DrawString($"{max:F1} °Brix", font, Brushes.White, x0 - 55, y0 - 5);
            g.DrawString($"{min:F1} °Brix", font, Brushes.White, x0 - 55, y0 + barH - 10);
        }
    }
}