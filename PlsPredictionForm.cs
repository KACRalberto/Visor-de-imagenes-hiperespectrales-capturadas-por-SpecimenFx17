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
    public class PlsPredictionForm : WeifenLuo.WinFormsUI.Docking.DockContent
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
            Size = new Size(1000, 700);
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
                Padding = new Padding(10),
                BackColor = Color.FromArgb(22, 22, 34)
            };

            // FIX 4K: Botones estirables con Padding en lugar de MinimumSize
            var btnExport = new Button { Text = "📥 1. Exportar Espectros (CSV)", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(15, 8, 15, 8), BackColor = Color.FromArgb(40, 90, 140), FlatStyle = FlatStyle.Flat };
            var btnLoadModel = new Button { Text = "📂 2. Cargar Modelo PLS", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(15, 8, 15, 8), BackColor = Color.FromArgb(110, 40, 110), FlatStyle = FlatStyle.Flat };
            var btnPredict = new Button { Text = "🔥 3. Generar Mapa °Brix", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(15, 8, 15, 8), BackColor = Color.FromArgb(35, 110, 55), FlatStyle = FlatStyle.Flat, Enabled = false };

            _pb = new ProgressBar { MinimumSize = new Size(150, 25), Visible = false, Style = ProgressBarStyle.Continuous, Margin = new Padding(15, 12, 5, 5) };
            _lblStatus = new Label { AutoSize = true, ForeColor = Color.FromArgb(150, 200, 150), Margin = new Padding(5, 15, 5, 5) };

            btnExport.Click += ExportSpectraToCsv;
            btnLoadModel.Click += (s, e) => { LoadPlsModel(); btnPredict.Enabled = _plsCoefs != null; };
            btnPredict.Click += GenerateBrixMap;

            pnlTop.Controls.AddRange(new Control[] { btnExport, btnLoadModel, btnPredict, _pb, _lblStatus });
            _picBrixMap = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };

            Controls.Add(_picBrixMap);
            Controls.Add(pnlTop);
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
                {
                    string wLabel = _cube.Header.Wavelengths != null && _cube.Header.Wavelengths.Count > b
                        ? _cube.Header.Wavelengths[b].ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
                        : b.ToString();
                    header.Append($"Wl_{wLabel}{(b == bands - 1 ? "" : ",")}");
                }
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
                if (lines.Length < 2) throw new Exception("El archivo es demasiado corto.");

                // 🛠️ FIX: Leer la Fila 1 (Índice 1), no la Fila 0 que tiene las cabeceras de texto
                var dataLine = lines[1].Split(',');

                // dataLine[0] es la palabra "Coeficientes"
                // dataLine[1] es el Intercepto numérico
                if (dataLine.Length < 2 || !double.TryParse(dataLine[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _plsIntercept))
                    throw new Exception("No se pudo leer el intercepto del modelo en la línea de datos.");

                // 🛠️ FIX: Los coeficientes reales empiezan a partir de la columna 2. Hacemos Skip(2).
                var coefStrings = dataLine.Skip(2).ToArray();
                _plsCoefs = coefStrings.Select(s => double.Parse(s.Trim(), System.Globalization.CultureInfo.InvariantCulture)).ToArray();

                _lblStatus.Text = $"Modelo cargado: {_plsCoefs.Length} coeficientes. Intercepto: {_plsIntercept:F3}";

                // Habilitar el botón de predicción que tengas en la UI
                // (Dependiendo de cómo lo llamaste, puede ser btnPredict.Enabled = true;)
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar el modelo: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _plsCoefs = null;
            }
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
                int maxBands = Math.Min(bands, _plsCoefs.Length);

                var validBrix = new List<float>(lines * samples);
                object syncObj = new object();

                Parallel.For(0, lines, () => new List<float>(), (y, loopState, localList) =>
                {
                    for (int x = 0; x < samples; x++)
                    {
                        if (!mask[y, x]) { brixMap[y, x] = float.NaN; continue; }

                        double pred = _plsIntercept;
                        for (int b = 0; b < maxBands; b++)
                        {
                            float v = _cube[b, y, x];
                            if (float.IsNaN(v) || float.IsInfinity(v)) v = 0f;
                            pred += v * _plsCoefs[b];
                        }

                        float val = (float)pred;
                        if (float.IsNaN(val) || float.IsInfinity(val)) val = 0f;

                        brixMap[y, x] = val;
                        localList.Add(val);
                    }
                    return localList;
                },
                localList => { lock (syncObj) validBrix.AddRange(localList); });

                float minBrix = 0, maxBrix = 1;
                if (validBrix.Count > 0)
                {
                    validBrix.Sort();
                    int idxMin = (int)(validBrix.Count * 0.02);
                    int idxMax = (int)(validBrix.Count * 0.98);
                    idxMax = Math.Clamp(idxMax, 0, validBrix.Count - 1);
                    minBrix = validBrix[idxMin];
                    maxBrix = validBrix[idxMax];
                }

                var bMap = new Bitmap(samples, lines, PixelFormat.Format24bppRgb);
                var bd = bMap.LockBits(new Rectangle(0, 0, samples, lines), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
                byte[] pixels = new byte[bd.Stride * lines];

                float range = maxBrix - minBrix;
                if (range <= 0.0001f) range = 1f;

                var (bgMin, bgMax) = _cube.GetBandStats(0);
                float bgRange = bgMax - bgMin;
                if (bgRange <= 0.0001f) bgRange = 1f;

                Parallel.For(0, lines, y =>
                {
                    int row = y * bd.Stride;
                    for (int x = 0; x < samples; x++)
                    {
                        int off = row + x * 3;
                        if (float.IsNaN(brixMap[y, x]))
                        {
                            float bgV = _cube[0, y, x];
                            if (float.IsNaN(bgV) || float.IsInfinity(bgV)) bgV = bgMin;
                            byte gray = (byte)(Math.Clamp((bgV - bgMin) / bgRange, 0f, 1f) * 255 * 0.2f);
                            pixels[off] = gray; pixels[off + 1] = gray; pixels[off + 2] = gray;
                            continue;
                        }

                        float t = Math.Clamp((brixMap[y, x] - minBrix) / range, 0f, 1f);
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