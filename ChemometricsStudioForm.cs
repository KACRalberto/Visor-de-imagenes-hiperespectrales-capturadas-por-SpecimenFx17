using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace SpecimenFX17.Imaging
{
    public class ChemometricsStudioForm : WeifenLuo.WinFormsUI.Docking.DockContent
    {
        private PcaResult? _pcaResult;
        private string _csvPath = "";

        // UI Controls
        private PictureBox _picScores = null!;
        private PictureBox _picLoadings = null!;
        private Label _lblInfo = null!;
        private ListBox _lstClasses = null!;

        // Paleta de colores para clases
        private readonly Color[] _classColors = { Color.Cyan, Color.Orange, Color.LimeGreen, Color.Magenta, Color.Yellow, Color.White };
        private Dictionary<string, Color> _classColorMap = new();

        public ChemometricsStudioForm()
        {
            Text = "Chemometrics Studio - Explorador PCA";
            Size = new System.Drawing.Size(1200, 800);
            BackColor = Color.FromArgb(20, 20, 25);
            ForeColor = Color.White;

            BuildUI();
        }

        private void BuildUI()
        {
            var pnlTop = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Color.FromArgb(30, 30, 35), Padding = new Padding(10) };
            var btnLoad = new Button { Text = "📂 Cargar Matriz CSV", AutoSize = true, BackColor = Color.FromArgb(0, 120, 215), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnLoad.Click += BtnLoad_Click;
            _lblInfo = new Label { Text = "Esperando datos...", AutoSize = true, Location = new System.Drawing.Point(160, 15), ForeColor = Color.Gray };

            pnlTop.Controls.Add(btnLoad);
            pnlTop.Controls.Add(_lblInfo);

            var splitMain = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 450, BackColor = Color.FromArgb(40, 40, 45) };

            // PANEL SUPERIOR: SCORES
            var pnlScores = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(15, 15, 20), Padding = new Padding(10) };
            var lblScoreTitle = new Label { Text = "📊 PCA Scores (PC1 vs PC2) - Agrupación de Muestras", Dock = DockStyle.Top, Height = 25, ForeColor = Color.LightSkyBlue, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
            _picScores = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(10, 10, 15) };
            _picScores.Paint += PaintScores;
            _picScores.Resize += (s, e) => _picScores.Invalidate();

            var pnlLegend = new Panel { Dock = DockStyle.Right, Width = 150, BackColor = Color.FromArgb(20, 20, 25) };
            _lstClasses = new ListBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(20, 20, 25), ForeColor = Color.White, BorderStyle = BorderStyle.None, SelectionMode = SelectionMode.None };
            pnlLegend.Controls.Add(_lstClasses);
            pnlLegend.Controls.Add(new Label { Text = "Clases:", Dock = DockStyle.Top, ForeColor = Color.Gray });

            pnlScores.Controls.Add(_picScores);
            pnlScores.Controls.Add(pnlLegend);
            pnlScores.Controls.Add(lblScoreTitle);

            // PANEL INFERIOR: LOADINGS
            var pnlLoadings = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(15, 15, 20), Padding = new Padding(10) };
            var lblLoadingTitle = new Label { Text = "📉 PCA Loadings (PC1) - Importancia de Longitudes de Onda", Dock = DockStyle.Top, Height = 25, ForeColor = Color.Orange, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
            _picLoadings = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(10, 10, 15) };
            _picLoadings.Paint += PaintLoadings;
            _picLoadings.Resize += (s, e) => _picLoadings.Invalidate();

            pnlLoadings.Controls.Add(_picLoadings);
            pnlLoadings.Controls.Add(lblLoadingTitle);

            splitMain.Panel1.Controls.Add(pnlScores);
            splitMain.Panel2.Controls.Add(pnlLoadings);

            Controls.Add(splitMain);
            Controls.Add(pnlTop);
        }

        private void BtnLoad_Click(object? sender, EventArgs e)
        {
            using var ofd = new OpenFileDialog { Filter = "Archivos CSV (*.csv)|*.csv" };
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    _csvPath = ofd.FileName;
                    _lblInfo.Text = "Calculando PCA... Espere...";
                    Application.DoEvents();

                    _pcaResult = PcaEngine.CalculatePcaFromCsv(_csvPath);

                    // Asignar colores a las clases encontradas
                    _classColorMap.Clear();
                    _lstClasses.Items.Clear();
                    int colorIdx = 0;
                    foreach (var c in _pcaResult.Classes.Distinct())
                    {
                        _classColorMap[c] = _classColors[colorIdx % _classColors.Length];
                        _lstClasses.Items.Add($"■ {c}");
                        colorIdx++;
                    }

                    _lblInfo.Text = $"Archivo: {System.IO.Path.GetFileName(_csvPath)} | Muestras: {_pcaResult.SampleIds.Count} | Varianza PC1: {_pcaResult.ExplainedVariance[0]:F1}% | PC2: {_pcaResult.ExplainedVariance[1]:F1}%";

                    _picScores.Invalidate();
                    _picLoadings.Invalidate();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error analizando CSV:\n{ex.Message}", "Error PCA", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    _lblInfo.Text = "Error en el cálculo.";
                }
            }
        }

        private void PaintScores(object? sender, PaintEventArgs e)
        {
            if (_pcaResult == null) return;

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int w = _picScores.Width;
            int h = _picScores.Height;

            // Encontrar Min/Max para escalar
            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;

            for (int i = 0; i < _pcaResult.SampleIds.Count; i++)
            {
                double x = _pcaResult.Scores[i, 0]; // PC1
                double y = _pcaResult.Scores[i, 1]; // PC2
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
            }

            // Márgenes
            double marginX = (maxX - minX) * 0.1; if (marginX == 0) marginX = 1;
            double marginY = (maxY - minY) * 0.1; if (marginY == 0) marginY = 1;
            minX -= marginX; maxX += marginX;
            minY -= marginY; maxY += marginY;

            // Dibujar Ejes (Cruces en el origen 0,0)
            int zeroX = (int)((0 - minX) / (maxX - minX) * w);
            int zeroY = h - (int)((0 - minY) / (maxY - minY) * h);

            using var axisPen = new Pen(Color.FromArgb(60, 255, 255, 255), 1) { DashStyle = DashStyle.Dash };
            if (zeroX >= 0 && zeroX <= w) g.DrawLine(axisPen, zeroX, 0, zeroX, h);
            if (zeroY >= 0 && zeroY <= h) g.DrawLine(axisPen, 0, zeroY, w, zeroY);

            // Dibujar Puntos
            int dotSize = 8;
            for (int i = 0; i < _pcaResult.SampleIds.Count; i++)
            {
                double px = _pcaResult.Scores[i, 0];
                double py = _pcaResult.Scores[i, 1];
                string cls = _pcaResult.Classes[i];

                int screenX = (int)((px - minX) / (maxX - minX) * w);
                int screenY = h - (int)((py - minY) / (maxY - minY) * h); // Invertir Y para gráficos

                Color c = _classColorMap.ContainsKey(cls) ? _classColorMap[cls] : Color.Gray;
                using var brush = new SolidBrush(Color.FromArgb(200, c));
                g.FillEllipse(brush, screenX - dotSize / 2, screenY - dotSize / 2, dotSize, dotSize);
                g.DrawEllipse(Pens.White, screenX - dotSize / 2, screenY - dotSize / 2, dotSize, dotSize);
            }
        }

        private void PaintLoadings(object? sender, PaintEventArgs e)
        {
            if (_pcaResult == null) return;

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int w = _picLoadings.Width;
            int h = _picLoadings.Height;

            int numVars = _pcaResult.Wavelengths.Count;
            if (numVars == 0) return;

            double minL = double.MaxValue, maxL = double.MinValue;
            for (int j = 0; j < numVars; j++)
            {
                double v = _pcaResult.Loadings[j, 0]; // Loadings de PC1
                if (v < minL) minL = v;
                if (v > maxL) maxL = v;
            }

            double marginY = (maxL - minL) * 0.1; if (marginY == 0) marginY = 1;
            minL -= marginY; maxL += marginY;

            // Eje Cero
            int zeroY = h - (int)((0 - minL) / (maxL - minL) * h);
            using var axisPen = new Pen(Color.FromArgb(80, 255, 255, 255), 1) { DashStyle = DashStyle.Dash };
            if (zeroY >= 0 && zeroY <= h) g.DrawLine(axisPen, 0, zeroY, w, zeroY);

            // Trazar línea de Loadings
            var points = new List<System.Drawing.PointF>();
            for (int j = 0; j < numVars; j++)
            {
                float px = (float)j / (numVars - 1) * w;
                float py = h - (float)((_pcaResult.Loadings[j, 0] - minL) / (maxL - minL) * h);
                points.Add(new System.Drawing.PointF(px, py));
            }

            if (points.Count > 1)
            {
                using var linePen = new Pen(Color.Orange, 2f);
                g.DrawLines(linePen, points.ToArray());
            }

            // Etiquetas de Longitud de Onda (Aprox 5 etiquetas)
            using var font = new Font("Consolas", 8f);
            for (int i = 0; i <= 5; i++)
            {
                int idx = i * (numVars - 1) / 5;
                float px = (float)idx / (numVars - 1) * w;
                string label = $"{_pcaResult.Wavelengths[idx]:F0}";
                g.DrawString(label, font, Brushes.Gray, px, h - 20);
            }
        }
    }
}