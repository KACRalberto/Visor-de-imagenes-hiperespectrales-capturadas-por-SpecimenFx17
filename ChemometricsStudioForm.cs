using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SpecimenFX17.Imaging
{
    public class ChemometricsStudioForm : WeifenLuo.WinFormsUI.Docking.DockContent
    {
        private List<string> _datasetLines = new();
        private PcaResult? _pcaResult;

        private PictureBox _picScores = null!;
        private PictureBox _picLoadings = null!;
        private Label _lblInfo = null!;
        private ListBox _lstClasses = null!;

        // 🔥 NUEVOS CONTROLES PARA PCA
        private NumericUpDown _nudComponents = null!;
        private Button _btnRecalculate = null!;
        private ComboBox _cmbXAxis = null!;
        private ComboBox _cmbYAxis = null!;

        private List<PointF> _scorePoints = new();
        private int _hoveredIndex = -1;

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
            // Panel Superior con FlowLayoutPanel para adaptar botones
            var pnlTop = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                BackColor = Color.FromArgb(30, 30, 35),
                Padding = new Padding(10)
            };

            var btnLoad = new Button { Text = "📂 Cargar Matriz CSV", AutoSize = true, Padding = new Padding(5), BackColor = Color.FromArgb(0, 120, 215), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, 20, 0) };
            btnLoad.Click += BtnLoad_Click;

            var lblComps = new Label { Text = "Nº Componentes (PCA):", AutoSize = true, ForeColor = Color.LightGray, Margin = new Padding(0, 8, 5, 0) };
            _nudComponents = new NumericUpDown { Minimum = 2, Maximum = 50, Value = 3, Width = 60, BackColor = Color.FromArgb(45, 45, 50), ForeColor = Color.White, Margin = new Padding(0, 5, 10, 0) };

            _btnRecalculate = new Button { Text = "🔄 Recalcular", AutoSize = true, Padding = new Padding(5), BackColor = Color.FromArgb(80, 50, 120), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Enabled = false, Margin = new Padding(0, 0, 20, 0) };
            _btnRecalculate.Click += async (s, e) => await RecalculatePCAAsync();

            _lblInfo = new Label { Text = "Esperando datos...", AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(0, 8, 0, 0) };

            pnlTop.Controls.Add(btnLoad);
            pnlTop.Controls.Add(lblComps);
            pnlTop.Controls.Add(_nudComponents);
            pnlTop.Controls.Add(_btnRecalculate);
            pnlTop.Controls.Add(_lblInfo);

            var splitMain = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 450, BackColor = Color.FromArgb(40, 40, 45) };

            // Panel de Scores
            var pnlScores = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(15, 15, 20), Padding = new Padding(10) };

            var pnlScoreHeader = new Panel { Dock = DockStyle.Top, Height = 35 };
            var lblScoreTitle = new Label { Text = "📊 PCA Scores - CLIC DERECHO BORRA OUTLIERS", Dock = DockStyle.Left, Width = 400, ForeColor = Color.LightSkyBlue, Font = new Font("Segoe UI", 10f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };

            // Controles de Ejes
            var pnlAxes = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 300, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 3, 0, 0) };
            var lblX = new Label { Text = "Eje X:", AutoSize = true, ForeColor = Color.White, Margin = new Padding(5, 5, 0, 0) };
            _cmbXAxis = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70, BackColor = Color.FromArgb(45, 45, 50), ForeColor = Color.White };
            var lblY = new Label { Text = "Eje Y:", AutoSize = true, ForeColor = Color.White, Margin = new Padding(15, 5, 0, 0) };
            _cmbYAxis = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70, BackColor = Color.FromArgb(45, 45, 50), ForeColor = Color.White };

            _cmbXAxis.SelectedIndexChanged += (s, e) => { _picScores.Invalidate(); _picLoadings.Invalidate(); };
            _cmbYAxis.SelectedIndexChanged += (s, e) => _picScores.Invalidate();

            pnlAxes.Controls.Add(lblX);
            pnlAxes.Controls.Add(_cmbXAxis);
            pnlAxes.Controls.Add(lblY);
            pnlAxes.Controls.Add(_cmbYAxis);

            pnlScoreHeader.Controls.Add(pnlAxes);
            pnlScoreHeader.Controls.Add(lblScoreTitle);

            _picScores = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(10, 10, 15) };
            _picScores.Paint += PaintScores;
            _picScores.MouseMove += PicScores_MouseMove;
            _picScores.MouseDown += PicScores_MouseDown;
            _picScores.Resize += (s, e) => _picScores.Invalidate();

            var pnlLegend = new Panel { Dock = DockStyle.Right, Width = 150, BackColor = Color.FromArgb(20, 20, 25) };
            _lstClasses = new ListBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(20, 20, 25), ForeColor = Color.White, BorderStyle = BorderStyle.None, SelectionMode = SelectionMode.None };
            pnlLegend.Controls.Add(_lstClasses);
            pnlLegend.Controls.Add(new Label { Text = "Clases:", Dock = DockStyle.Top, ForeColor = Color.Gray });

            pnlScores.Controls.Add(_picScores);
            pnlScores.Controls.Add(pnlLegend);
            pnlScores.Controls.Add(pnlScoreHeader);

            // Panel de Loadings
            var pnlLoadings = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(15, 15, 20), Padding = new Padding(10) };
            var lblLoadingTitle = new Label { Text = "📉 PCA Loadings (Eje X) - Importancia de Longitudes de Onda", Dock = DockStyle.Top, Height = 25, ForeColor = Color.Orange, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
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

        private async void BtnLoad_Click(object? sender, EventArgs e)
        {
            using var ofd = new OpenFileDialog { Filter = "Archivos CSV (*.csv)|*.csv" };
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    _datasetLines = System.IO.File.ReadAllLines(ofd.FileName).ToList();
                    await RecalculatePCAAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error leyendo archivo:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private async Task RecalculatePCAAsync()
        {
            if (_datasetLines.Count < 3) return;

            _lblInfo.Text = "⏳ Calculando matriz PCA de alta dimensión...";
            _lblInfo.ForeColor = Color.Orange;
            _btnRecalculate.Enabled = false;

            int numComps = (int)_nudComponents.Value;

            try
            {
                _pcaResult = await Task.Run(() => PcaEngine.CalculatePca(_datasetLines.ToArray(), numComps));

                _classColorMap.Clear();
                _lstClasses.Items.Clear();
                int colorIdx = 0;
                foreach (var c in _pcaResult.Classes.Distinct())
                {
                    _classColorMap[c] = _classColors[colorIdx % _classColors.Length];
                    _lstClasses.Items.Add($"■ {c}");
                    colorIdx++;
                }

                // Guardamos la selección anterior de ejes para no perderla si es válida
                int oldX = _cmbXAxis.SelectedIndex;
                int oldY = _cmbYAxis.SelectedIndex;

                _cmbXAxis.Items.Clear();
                _cmbYAxis.Items.Clear();
                for (int i = 0; i < numComps; i++)
                {
                    _cmbXAxis.Items.Add($"PC{i + 1}");
                    _cmbYAxis.Items.Add($"PC{i + 1}");
                }

                _cmbXAxis.SelectedIndex = (oldX >= 0 && oldX < numComps) ? oldX : 0;
                _cmbYAxis.SelectedIndex = (oldY >= 0 && oldY < numComps) ? oldY : (numComps > 1 ? 1 : 0);

                _lblInfo.Text = $"✅ Muestras: {_pcaResult.SampleIds.Count} | Varianza PC1: {_pcaResult.ExplainedVariance[0]:F1}% | PC2: {((numComps > 1) ? _pcaResult.ExplainedVariance[1].ToString("F1") : "0.0")}%";
                _lblInfo.ForeColor = Color.LightGreen;

                _hoveredIndex = -1;
                _picScores.Invalidate();
                _picLoadings.Invalidate();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error PCA", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _lblInfo.Text = "❌ Error de estructura en el CSV.";
                _lblInfo.ForeColor = Color.Red;
            }
            finally
            {
                _btnRecalculate.Enabled = true;
            }
        }

        private void PicScores_MouseMove(object? sender, MouseEventArgs e)
        {
            if (_pcaResult == null || _scorePoints.Count != _pcaResult.SampleIds.Count) return;

            int closestIndex = -1;
            double minDistance = 10.0;

            for (int i = 0; i < _scorePoints.Count; i++)
            {
                double dist = Math.Sqrt(Math.Pow(e.X - _scorePoints[i].X, 2) + Math.Pow(e.Y - _scorePoints[i].Y, 2));
                if (dist < minDistance)
                {
                    minDistance = dist;
                    closestIndex = i;
                }
            }

            if (_hoveredIndex != closestIndex)
            {
                _hoveredIndex = closestIndex;
                _picScores.Cursor = (_hoveredIndex != -1) ? Cursors.Hand : Cursors.Default;
                _picScores.Invalidate();
            }
        }

        private async void PicScores_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right && _hoveredIndex != -1 && _pcaResult != null)
            {
                string id = _pcaResult.SampleIds[_hoveredIndex];
                string cls = _pcaResult.Classes[_hoveredIndex];

                var result = MessageBox.Show(
                    $"¿Deseas eliminar definitivamente el punto anómalo '{id}' (Clase: {cls}) del análisis PCA?\n\nEl modelo se recalculará automáticamente.",
                    "Eliminar Outlier", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                if (result == DialogResult.Yes)
                {
                    _datasetLines.RemoveAt(_hoveredIndex + 1);
                    await RecalculatePCAAsync();
                }
            }
        }

        private void PaintScores(object? sender, PaintEventArgs e)
        {
            if (_pcaResult == null || _cmbXAxis.SelectedIndex < 0 || _cmbYAxis.SelectedIndex < 0) return;

            int idxX = _cmbXAxis.SelectedIndex;
            int idxY = _cmbYAxis.SelectedIndex;

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int w = _picScores.Width;
            int h = _picScores.Height;

            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;

            for (int i = 0; i < _pcaResult.SampleIds.Count; i++)
            {
                double x = _pcaResult.Scores[i, idxX];
                double y = _pcaResult.Scores[i, idxY];
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
            }

            double marginX = (maxX - minX) * 0.1; if (marginX == 0) marginX = 1;
            double marginY = (maxY - minY) * 0.1; if (marginY == 0) marginY = 1;
            minX -= marginX; maxX += marginX;
            minY -= marginY; maxY += marginY;

            int zeroX = (int)((0 - minX) / (maxX - minX) * w);
            int zeroY = h - (int)((0 - minY) / (maxY - minY) * h);

            // 🔥 DIBUJO DE LOS EJES 0 CLAROS
            using var axisPen = new Pen(Color.FromArgb(150, 255, 255, 255), 2f) { DashStyle = DashStyle.Dash };
            if (zeroX >= 0 && zeroX <= w) g.DrawLine(axisPen, zeroX, 0, zeroX, h);
            if (zeroY >= 0 && zeroY <= h) g.DrawLine(axisPen, 0, zeroY, w, zeroY);

            _scorePoints.Clear();

            for (int i = 0; i < _pcaResult.SampleIds.Count; i++)
            {
                int screenX = (int)((_pcaResult.Scores[i, idxX] - minX) / (maxX - minX) * w);
                int screenY = h - (int)((_pcaResult.Scores[i, idxY] - minY) / (maxY - minY) * h);
                _scorePoints.Add(new PointF(screenX, screenY));

                if (i == _hoveredIndex) continue;

                Color c = _classColorMap.ContainsKey(_pcaResult.Classes[i]) ? _classColorMap[_pcaResult.Classes[i]] : Color.Gray;
                using var brush = new SolidBrush(Color.FromArgb(180, c));
                g.FillEllipse(brush, screenX - 4, screenY - 4, 8, 8);
                g.DrawEllipse(Pens.White, screenX - 4, screenY - 4, 8, 8);
            }

            if (_hoveredIndex != -1 && _hoveredIndex < _scorePoints.Count)
            {
                int hX = (int)_scorePoints[_hoveredIndex].X;
                int hY = (int)_scorePoints[_hoveredIndex].Y;
                Color hc = _classColorMap.ContainsKey(_pcaResult.Classes[_hoveredIndex]) ? _classColorMap[_pcaResult.Classes[_hoveredIndex]] : Color.White;

                g.FillEllipse(new SolidBrush(hc), hX - 6, hY - 6, 12, 12);
                g.DrawEllipse(new Pen(Color.White, 2f), hX - 6, hY - 6, 12, 12);

                string label = $"{_pcaResult.SampleIds[_hoveredIndex]} ({_pcaResult.Classes[_hoveredIndex]})";
                using var font = new Font("Segoe UI", 9f, FontStyle.Bold);
                var sz = g.MeasureString(label, font);

                float boxWidth = sz.Width + 6;
                float boxHeight = sz.Height + 6;
                float drawX = hX + 12;
                float drawY = hY - 12;

                if (drawX + boxWidth > w) drawX = hX - boxWidth - 12;
                if (drawY < 0) drawY = hY + 12;
                if (drawY + boxHeight > h) drawY = h - boxHeight - 12;

                g.FillRectangle(new SolidBrush(Color.FromArgb(230, 20, 20, 25)), drawX, drawY, boxWidth, boxHeight);
                g.DrawRectangle(Pens.Gray, drawX, drawY, boxWidth, boxHeight);
                g.DrawString(label, font, Brushes.White, drawX + 3, drawY + 3);
            }

            // 🔥 TÍTULOS DE LOS EJES
            using var titleFont = new Font("Segoe UI", 10f, FontStyle.Bold);
            g.DrawString($"Eje X: PC{idxX + 1} ({_pcaResult.ExplainedVariance[idxX]:F1}%)", titleFont, Brushes.LightGray, w / 2 - 60, h - 25);

            var state = g.Save();
            g.TranslateTransform(25, h / 2 + 60);
            g.RotateTransform(-90);
            g.DrawString($"Eje Y: PC{idxY + 1} ({_pcaResult.ExplainedVariance[idxY]:F1}%)", titleFont, Brushes.LightGray, 0, 0);
            g.Restore(state);
        }

        private void PaintLoadings(object? sender, PaintEventArgs e)
        {
            if (_pcaResult == null || _cmbXAxis.SelectedIndex < 0) return;

            int idxX = _cmbXAxis.SelectedIndex; // Se enlaza al eje X seleccionado

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int w = _picLoadings.Width;
            int h = _picLoadings.Height;

            int numVars = _pcaResult.Wavelengths.Count;
            if (numVars == 0) return;

            double minL = double.MaxValue, maxL = double.MinValue;
            for (int j = 0; j < numVars; j++)
            {
                double v = _pcaResult.Loadings[j, idxX];
                if (v < minL) minL = v;
                if (v > maxL) maxL = v;
            }

            double marginY = (maxL - minL) * 0.1; if (marginY == 0) marginY = 1;
            minL -= marginY; maxL += marginY;

            int zeroY = h - (int)((0 - minL) / (maxL - minL) * h);
            using var axisPen = new Pen(Color.FromArgb(80, 255, 255, 255), 1) { DashStyle = DashStyle.Dash };
            if (zeroY >= 0 && zeroY <= h) g.DrawLine(axisPen, 0, zeroY, w, zeroY);

            var points = new List<System.Drawing.PointF>();
            for (int j = 0; j < numVars; j++)
            {
                float px = (float)j / (numVars - 1) * w;
                float py = h - (float)((_pcaResult.Loadings[j, idxX] - minL) / (maxL - minL) * h);
                if (!float.IsNaN(px) && !float.IsNaN(py) && !float.IsInfinity(py))
                {
                    points.Add(new System.Drawing.PointF(px, py));
                }
            }

            if (points.Count > 1)
            {
                using var linePen = new Pen(Color.Orange, 2f);
                g.DrawLines(linePen, points.ToArray());
            }

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