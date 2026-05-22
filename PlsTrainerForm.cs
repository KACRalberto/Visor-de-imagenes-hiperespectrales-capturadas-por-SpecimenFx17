using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SpecimenFX17.Imaging
{
    public class PlsTrainerForm : WeifenLuo.WinFormsUI.Docking.DockContent
    {
        private List<string[]> _csvData = new();
        private List<string> _headers = new();

        private ComboBox _cmbTargetY = null!;
        private NumericUpDown _nudComponents = null!;
        private Button _btnTrain = null!;
        private Button _btnExportModel = null!;

        private Label _lblMetrics = null!;
        private PictureBox _picScatter = null!;

        private PlsModel? _trainedModel;

        public PlsTrainerForm()
        {
            Text = "Entrenamiento de Modelos PLS (°Brix)";
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
                Padding = new Padding(10),
                BackColor = Color.FromArgb(22, 22, 34)
            };

            var btnLoad = new Button { Text = "📂 1. Cargar CSV (Espectros + Brix)", AutoSize = true, Padding = new Padding(10, 5, 10, 5), BackColor = Color.FromArgb(40, 90, 140), FlatStyle = FlatStyle.Flat };
            btnLoad.Click += BtnLoad_Click;

            var lblTarget = new Label { Text = "Variable Objetivo (Y):", AutoSize = true, Margin = new Padding(15, 10, 0, 0) };
            _cmbTargetY = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(45, 45, 50), ForeColor = Color.White, Width = 150, Margin = new Padding(5) };

            var lblComp = new Label { Text = "Componentes (LV):", AutoSize = true, Margin = new Padding(15, 10, 0, 0) };
            _nudComponents = new NumericUpDown { Minimum = 1, Maximum = 50, Value = 5, Width = 60, BackColor = Color.FromArgb(45, 45, 50), ForeColor = Color.White, Margin = new Padding(5) };

            _btnTrain = new Button { Text = "🧠 2. Entrenar PLS", AutoSize = true, Padding = new Padding(10, 5, 10, 5), BackColor = Color.FromArgb(110, 40, 110), FlatStyle = FlatStyle.Flat, Enabled = false, Margin = new Padding(15, 5, 0, 5) };
            _btnTrain.Click += BtnTrain_Click;

            _btnExportModel = new Button { Text = "💾 3. Exportar Modelo", AutoSize = true, Padding = new Padding(10, 5, 10, 5), BackColor = Color.FromArgb(35, 110, 55), FlatStyle = FlatStyle.Flat, Enabled = false, Margin = new Padding(15, 5, 0, 5) };
            _btnExportModel.Click += BtnExport_Click;

            pnlTop.Controls.AddRange(new Control[] { btnLoad, lblTarget, _cmbTargetY, lblComp, _nudComponents, _btnTrain, _btnExportModel });

            var pnlMain = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 100, BackColor = Color.FromArgb(30, 30, 35) };

            _lblMetrics = new Label { Dock = DockStyle.Fill, Font = new Font("Consolas", 12f), ForeColor = Color.LightGreen, TextAlign = ContentAlignment.MiddleCenter, Text = "Carga un CSV para comenzar." };
            pnlMain.Panel1.Controls.Add(_lblMetrics);

            _picScatter = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(12, 12, 20) };
            _picScatter.Paint += DrawScatterPlot;
            _picScatter.Resize += (s, e) => _picScatter.Invalidate();
            pnlMain.Panel2.Controls.Add(_picScatter);

            Controls.Add(pnlMain);
            Controls.Add(pnlTop);
        }

        private void BtnLoad_Click(object? sender, EventArgs e)
        {
            using var ofd = new OpenFileDialog { Filter = "Archivos CSV (*.csv)|*.csv" };
            if (ofd.ShowDialog() != DialogResult.OK) return;

            try
            {
                var lines = File.ReadAllLines(ofd.FileName);
                if (lines.Length < 2) throw new Exception("El CSV está vacío.");

                _headers = lines[0].Split(',').Select(h => h.Trim()).ToList();
                _csvData.Clear();

                for (int i = 1; i < lines.Length; i++)
                    _csvData.Add(lines[i].Split(','));

                _cmbTargetY.Items.Clear();
                foreach (var h in _headers) _cmbTargetY.Items.Add(h);

                // Intentar autoseleccionar una columna llamada "Brix" o similar
                int defaultIdx = _headers.FindIndex(h => h.Contains("Brix", StringComparison.OrdinalIgnoreCase) || h.Contains("Target", StringComparison.OrdinalIgnoreCase));
                _cmbTargetY.SelectedIndex = defaultIdx >= 0 ? defaultIdx : 0;

                _btnTrain.Enabled = true;
                _lblMetrics.Text = $"CSV Cargado: {_csvData.Count} muestras encontradas.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar el CSV: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnTrain_Click(object? sender, EventArgs e)
        {
            if (_csvData.Count == 0 || _cmbTargetY.SelectedIndex < 0) return;

            int targetIdx = _cmbTargetY.SelectedIndex;
            int numSamples = _csvData.Count;

            // Asumimos que todas las columnas que pueden parsearse a Double y NO son el Target, son variables X (Espectros)
            var xIndices = new List<int>();
            for (int j = 0; j < _headers.Count; j++)
            {
                if (j == targetIdx) continue;
                // Verificamos si la primera fila es un número
                if (double.TryParse(_csvData[0][j], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _))
                    xIndices.Add(j);
            }

            int numVars = xIndices.Count;
            if (numVars == 0)
            {
                MessageBox.Show("No se detectaron columnas espectrales válidas (numéricas).", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            double[,] X = new double[numSamples, numVars];
            double[] Y = new double[numSamples];

            int validSamples = 0;
            for (int i = 0; i < numSamples; i++)
            {
                if (double.TryParse(_csvData[i][targetIdx], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double yVal))
                {
                    Y[validSamples] = yVal;
                    for (int j = 0; j < numVars; j++)
                    {
                        double.TryParse(_csvData[i][xIndices[j]], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double xVal);
                        X[validSamples, j] = xVal;
                    }
                    validSamples++;
                }
            }

            // Recortar si hubo filas fallidas
            if (validSamples < numSamples)
            {
                double[,] X_clean = new double[validSamples, numVars];
                double[] Y_clean = new double[validSamples];
                Array.Copy(Y, Y_clean, validSamples);
                for (int i = 0; i < validSamples; i++) for (int j = 0; j < numVars; j++) X_clean[i, j] = X[i, j];
                X = X_clean;
                Y = Y_clean;
            }

            int comps = Math.Min((int)_nudComponents.Value, Math.Min(validSamples - 1, numVars));

            try
            {
                _trainedModel = PlsEngine.TrainPLS1(X, Y, comps);
                _lblMetrics.Text = $"ENTRENAMIENTO COMPLETADO\nR²: {_trainedModel.R2:F4}   |   RMSE: {_trainedModel.RMSE:F4}";
                _picScatter.Tag = Y; // Guardamos el Y real en el Tag para el evento Paint
                _picScatter.Invalidate();
                _btnExportModel.Enabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error durante el entrenamiento PLS:\n{ex.Message}", "Error Matemático", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DrawScatterPlot(object? sender, PaintEventArgs e)
        {
            if (_trainedModel == null || _picScatter.Tag == null) return;

            double[] actualY = (double[])_picScatter.Tag;
            double[] predictedY = _trainedModel.PredictedY;

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int w = _picScatter.Width, h = _picScatter.Height;
            Rectangle rect = new Rectangle(50, 20, w - 80, h - 60);

            double minVal = Math.Min(actualY.Min(), predictedY.Min());
            double maxVal = Math.Max(actualY.Max(), predictedY.Max());
            double range = maxVal - minVal;
            if (range < 1e-9) range = 1;

            minVal -= range * 0.1;
            maxVal += range * 0.1;
            range = maxVal - minVal;

            using var gridPen = new Pen(Color.FromArgb(40, 255, 255, 255)) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(gridPen, rect);

            // Línea ideal (y = x)
            float x1 = rect.Left; float y1 = rect.Bottom;
            float x2 = rect.Right; float y2 = rect.Top;
            g.DrawLine(new Pen(Color.FromArgb(100, 255, 255, 255)), x1, y1, x2, y2);

            using var pointBrush = new SolidBrush(Color.Orange);
            for (int i = 0; i < actualY.Length; i++)
            {
                float px = rect.Left + (float)((actualY[i] - minVal) / range * rect.Width);
                float py = rect.Bottom - (float)((predictedY[i] - minVal) / range * rect.Height);
                g.FillEllipse(pointBrush, px - 4, py - 4, 8, 8);
                g.DrawEllipse(Pens.White, px - 4, py - 4, 8, 8);
            }

            using var font = new Font("Segoe UI", 9f);
            g.DrawString("Real (°Brix Laboratorio)", font, Brushes.Gray, rect.Left + rect.Width / 2 - 50, rect.Bottom + 10);

            var state = g.Save();
            g.TranslateTransform(15, rect.Top + rect.Height / 2 + 50);
            g.RotateTransform(-90);
            g.DrawString("Predicho (Modelo PLS)", font, Brushes.Gray, 0, 0);
            g.Restore(state);
        }

        private void BtnExport_Click(object? sender, EventArgs e)
        {
            if (_trainedModel == null) return;

            using var sfd = new SaveFileDialog { Filter = "Modelo PLS CSV (*.csv)|*.csv", FileName = "Modelo_Brix_PLS.csv" };
            if (sfd.ShowDialog() == DialogResult.OK)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Tipo,Intercepto");
                sb.AppendLine($"Coeficientes,{_trainedModel.Intercept.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                              string.Join(",", _trainedModel.Coefficients.Select(c => c.ToString(System.Globalization.CultureInfo.InvariantCulture))));

                File.WriteAllText(sfd.FileName, sb.ToString());
                MessageBox.Show("Modelo exportado correctamente. Ya puedes cargarlo en la ventana de Predicción de Mapas °Brix.", "Éxito", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}