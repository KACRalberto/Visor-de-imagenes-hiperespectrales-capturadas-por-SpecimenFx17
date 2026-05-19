using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenCvSharp;

namespace SpecimenFX17.Imaging
{
    public class SequentialBatchForm : WeifenLuo.WinFormsUI.Docking.DockContent
    {
        private readonly List<string> _files;
        private int _currentIndex = -1;
        private HyperspectralCube? _currentCube;
        private List<SelectionShape> _currentRois = new();
        private readonly BatchOptions _opts;
        private readonly string _outputPath;

        // Referencias de Calibración
        private readonly HyperspectralCube? _whiteRef;
        private readonly HyperspectralCube? _darkRef;

        // Controles UI
        private PictureBox _picView = null!;
        private Label _lblInfo = null!;
        private TextBox _txtClass = null!;
        private Button _btnNext = null!;
        private ProgressBar _pbProgress = null!;
        private StringBuilder _csvMatrix = new();

        // Controles para ordenar
        private ListBox _lstObjects = null!;
        private Button _btnUp = null!;
        private Button _btnDown = null!;

        public SequentialBatchForm(string inputFolder, string outputFolder, BatchOptions opts, HyperspectralCube? whiteCube = null, HyperspectralCube? darkCube = null)
        {
            _outputPath = outputFolder;
            _opts = opts;
            _whiteRef = whiteCube;
            _darkRef = darkCube;
            _files = Directory.GetFiles(inputFolder, "*.hdr").OrderBy(f => f).ToList();

            Text = "Asistente de Segmentación Secuencial";
            Size = new System.Drawing.Size(1200, 850);
            BackColor = Color.FromArgb(30, 30, 35);
            ForeColor = Color.White;

            BuildUI();
        }

        private void BuildUI()
        {
            var pnlTop = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Color.FromArgb(20, 20, 25), Padding = new Padding(10) };
            _lblInfo = new Label { Text = "Cargando...", AutoSize = true, Font = new Font("Segoe UI", 12f, FontStyle.Bold), ForeColor = Color.LightSkyBlue };
            _pbProgress = new ProgressBar { Dock = DockStyle.Bottom, Height = 5 };
            pnlTop.Controls.Add(_lblInfo);
            pnlTop.Controls.Add(_pbProgress);

            var pnlRight = new Panel { Dock = DockStyle.Right, Width = 320, BackColor = Color.FromArgb(25, 25, 30), Padding = new Padding(15) };

            _btnNext = new Button
            {
                Text = "SIGUIENTE IMAGEN ➔",
                Dock = DockStyle.Bottom,
                Height = 60,
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold)
            };
            _btnNext.Click += (s, e) => ProcessAndNext();

            var flp = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };

            var lblHint = new Label
            {
                Text = "💡 INSTRUCCIONES:\n1. Clic DERECHO en la imagen para borrar.\n2. Usa la lista para REORDENAR.\n3. Ajusta la Clase y pulsa SIGUIENTE.",
                AutoSize = true,
                ForeColor = Color.Gray,
                Font = new Font("Segoe UI", 9f, FontStyle.Italic),
                Margin = new Padding(0, 0, 0, 15)
            };

            var lblClass = new Label { Text = "Etiqueta / Clase de esta imagen:", AutoSize = true, ForeColor = Color.LightGray };
            _txtClass = new TextBox { Width = 290, BackColor = Color.FromArgb(45, 45, 50), ForeColor = Color.White, Font = new Font("Segoe UI", 11f), Margin = new Padding(0, 5, 0, 20) };

            var lblObj = new Label { Text = "Orden de Objetos Detectados:", AutoSize = true, ForeColor = Color.LightSkyBlue, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
            _lstObjects = new ListBox { Width = 290, Height = 180, BackColor = Color.FromArgb(45, 45, 50), ForeColor = Color.White, Font = new Font("Segoe UI", 11f), Margin = new Padding(0, 5, 0, 5) };
            _lstObjects.SelectedIndexChanged += (s, e) => _picView.Invalidate();

            var pnlBtns = new Panel { Width = 290, Height = 35, Margin = new Padding(0, 0, 0, 20) };
            _btnUp = new Button { Text = "⬆ Subir", Width = 140, Left = 0, BackColor = Color.FromArgb(60, 60, 65), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            _btnDown = new Button { Text = "⬇ Bajar", Width = 140, Left = 150, BackColor = Color.FromArgb(60, 60, 65), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };

            _btnUp.Click += BtnUp_Click;
            _btnDown.Click += BtnDown_Click;

            pnlBtns.Controls.Add(_btnUp);
            pnlBtns.Controls.Add(_btnDown);

            flp.Controls.Add(lblHint);
            flp.Controls.Add(lblClass);
            flp.Controls.Add(_txtClass);
            flp.Controls.Add(lblObj);
            flp.Controls.Add(_lstObjects);
            flp.Controls.Add(pnlBtns);

            pnlRight.Controls.Add(flp);
            pnlRight.Controls.Add(_btnNext);

            _picView = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black, Cursor = Cursors.Hand };
            _picView.MouseDown += PicView_MouseDown;
            _picView.Paint += PicView_Paint;

            Controls.Add(_picView);
            Controls.Add(pnlRight);
            Controls.Add(pnlTop);

            LoadNextImage();
        }

        private async void LoadNextImage()
        {
            _currentIndex++;
            if (_currentIndex >= _files.Count)
            {
                FinalizeBatch();
                return;
            }

            string path = _files[_currentIndex];
            if (this.IsDisposed) return;

            _lblInfo.Text = $"Procesando {_currentIndex + 1} de {_files.Count}: {Path.GetFileName(path)}";
            _lblInfo.ForeColor = Color.LightSkyBlue;
            _pbProgress.Value = (_currentIndex * 100) / _files.Count;
            _btnNext.Enabled = false;

            _picView.Image?.Dispose();
            _picView.Image = null;
            _picView.Invalidate();

            var oldCube = _currentCube;

            try
            {
                _currentCube = await Task.Run(() => {
                    oldCube?.Dispose();
                    var cube = HyperspectralCube.Load(path);

                    if (_opts.ApplyNormalize && _whiteRef != null && _darkRef != null)
                        cube.Calibrate(_whiteRef, _darkRef, System.Threading.CancellationToken.None);

                    if (_opts.ConvertToAbsorbance && cube.IsCalibrated)
                        cube.ConvertToAbsorbance(System.Threading.CancellationToken.None);

                    if (_opts.ApplySNV) cube.ApplySNV(System.Threading.CancellationToken.None);
                    if (_opts.ApplyMSC) cube.ApplyMSC(System.Threading.CancellationToken.None);
                    if (_opts.ApplySavitzkyGolay) cube.ApplySavitzkyGolay(_opts.SgWindow, _opts.SgPoly, _opts.SgDeriv, System.Threading.CancellationToken.None);
                    if (_opts.ApplyMedianFilter) cube.ApplySpatialMedianFilter(3, System.Threading.CancellationToken.None);

                    return cube;
                });

                if (this.IsDisposed) return;

                // Segmentación Automática 
                // NOTA: AutoSegmenter ahora acepta el CancellationToken, le pasamos None porque aquí no cancelamos a mano
                _currentRois = await AutoSegmenter.SegmentCubeAsync(_currentCube!, _opts.SegmentationBand, _opts.CustomParams, null, System.Threading.CancellationToken.None);

                if (this.IsDisposed) return;

                _txtClass.Text = Path.GetFileNameWithoutExtension(path).Split('_')[0];

                SyncObjectList();
                RefreshView();
            }
            catch (Exception ex)
            {
                // 🛡️ AMORTIGUADOR DE CORRUPCIÓN: Si una imagen falla, no paramos la fábrica.
                if (!this.IsDisposed)
                {
                    _csvMatrix.AppendLine($"ERROR,{Path.GetFileName(path)},El archivo está corrupto o es inaccesible: {ex.Message}");
                    _lblInfo.Text = $"⚠️ Archivo corrupto saltado: {Path.GetFileName(path)}";
                    _lblInfo.ForeColor = Color.Orange;

                    // Saltamos automáticamente a la siguiente imagen sin bloquear la pantalla
                    LoadNextImage();
                }
            }
            finally
            {
                if (!this.IsDisposed) _btnNext.Enabled = true;

                // 🧹 EL CAMIÓN DE LA BASURA (Fix Memory Leak): 
                // Forzamos la liberación de la RAM no administrada que deja OpenCV y los archivos .bil gigantes
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        private void SyncObjectList()
        {
            int sel = _lstObjects.SelectedIndex;
            _lstObjects.Items.Clear();
            for (int i = 0; i < _currentRois.Count; i++)
                _lstObjects.Items.Add($"Objeto {i + 1}");

            if (sel >= 0 && sel < _lstObjects.Items.Count)
                _lstObjects.SelectedIndex = sel;

            _picView.Invalidate();
        }

        private void BtnUp_Click(object? s, EventArgs e)
        {
            int idx = _lstObjects.SelectedIndex;
            if (idx <= 0) return;

            var temp = _currentRois[idx];
            _currentRois[idx] = _currentRois[idx - 1];
            _currentRois[idx - 1] = temp;

            SyncObjectList();
            _lstObjects.SelectedIndex = idx - 1;
        }

        private void BtnDown_Click(object? s, EventArgs e)
        {
            int idx = _lstObjects.SelectedIndex;
            if (idx < 0 || idx >= _lstObjects.Items.Count - 1) return;

            var temp = _currentRois[idx];
            _currentRois[idx] = _currentRois[idx + 1];
            _currentRois[idx + 1] = temp;

            SyncObjectList();
            _lstObjects.SelectedIndex = idx + 1;
        }

        private void RefreshView()
        {
            if (_currentCube == null) return;

            var renderOpts = new BliRenderOptions { Colormap = BliColormap.Grayscale, DrawColorbar = false };
            var bmp = BliRenderer.RenderBand(_currentCube, _opts.SegmentationBand, renderOpts);

            _picView.Image?.Dispose();
            _picView.Image = bmp;
        }

        private void PicView_Paint(object? sender, PaintEventArgs e)
        {
            if (_picView.Image == null) return;

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float ratio = Math.Min((float)_picView.Width / _currentCube!.Samples, (float)_picView.Height / _currentCube!.Lines);
            float offX = (_picView.Width - _currentCube.Samples * ratio) / 2;
            float offY = (_picView.Height - _currentCube.Lines * ratio) / 2;

            int count = 1;
            int selectedIndex = _lstObjects.SelectedIndex;

            foreach (var roi in _currentRois)
            {
                bool isSelected = (count - 1 == selectedIndex);
                using var p = new Pen(isSelected ? Color.White : roi.Color, isSelected ? 4 : 2);

                var mask = roi.GetMask(_currentCube.Lines, _currentCube.Samples);
                int minX = int.MaxValue, minY = int.MaxValue, maxX = 0, maxY = 0;
                for (int y = 0; y < _currentCube.Lines; y++)
                    for (int x = 0; x < _currentCube.Samples; x++)
                        if (mask[y, x])
                        {
                            if (x < minX) minX = x; if (x > maxX) maxX = x;
                            if (y < minY) minY = y; if (y > maxY) maxY = y;
                        }

                var rect = new RectangleF(offX + minX * ratio, offY + minY * ratio, (maxX - minX) * ratio, (maxY - minY) * ratio);
                g.DrawRectangle(p, rect.X, rect.Y, rect.Width, rect.Height);

                string txt = count.ToString();
                using var font = new Font("Arial", isSelected ? 18 : 14, FontStyle.Bold);
                var sz = g.MeasureString(txt, font);
                g.FillRectangle(new SolidBrush(isSelected ? Color.White : roi.Color), rect.X, rect.Y - sz.Height, sz.Width, sz.Height);
                g.DrawString(txt, font, Brushes.Black, rect.X, rect.Y - sz.Height);

                count++;
            }
        }

        private void PicView_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                float ratio = Math.Min((float)_picView.Width / _currentCube!.Samples, (float)_picView.Height / _currentCube!.Lines);
                float offX = (_picView.Width - _currentCube.Samples * ratio) / 2;
                float offY = (_picView.Height - _currentCube.Lines * ratio) / 2;

                int imgX = (int)((e.X - offX) / ratio);
                int imgY = (int)((e.Y - offY) / ratio);

                var toRemove = _currentRois.FirstOrDefault(r => r.Contains(new System.Drawing.Point(imgX, imgY)));
                if (toRemove != null)
                {
                    _currentRois.Remove(toRemove);
                    SyncObjectList();
                }
            }
        }

        private void ProcessAndNext()
        {
            if (_currentCube == null) return;

            if (_csvMatrix.Length == 0)
            {
                _csvMatrix.Append("ID_Muestra,Clase,Nombre_Archivo");
                for (int i = 0; i < _currentCube.Bands; i++)
                {
                    double wl = _currentCube.Header.Wavelengths.Count > i ? _currentCube.Header.Wavelengths[i] : i;
                    _csvMatrix.Append($",{wl.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}");
                }
                _csvMatrix.AppendLine();
            }

            int subId = 1;
            foreach (var roi in _currentRois)
            {
                var spectrum = roi.GetSpectrum(_currentCube);
                string fileName = Path.GetFileName(_files[_currentIndex]);
                string sampleId = $"{Path.GetFileNameWithoutExtension(fileName)}_{subId}";

                _csvMatrix.Append($"{sampleId},{_txtClass.Text},{fileName}");
                foreach (var val in spectrum)
                {
                    _csvMatrix.Append($",{val.ToString("G6", System.Globalization.CultureInfo.InvariantCulture)}");
                }
                _csvMatrix.AppendLine();
                subId++;
            }

            LoadNextImage();
        }

        private void FinalizeBatch()
        {
            string csvPath = Path.Combine(_outputPath, "Matriz_Espectral_Investigacion.csv");
            bool fileSaved = false;

            // 🛡️ EL ESCUDO CONTRA EXCEL: Un bucle infinito hasta que el usuario cierre el archivo o cancele
            while (!fileSaved)
            {
                try
                {
                    File.WriteAllText(csvPath, _csvMatrix.ToString(), Encoding.UTF8);
                    fileSaved = true; // Si llegamos aquí, se guardó con éxito
                }
                catch (IOException)
                {
                    var result = MessageBox.Show(
                        $"¡Alto ahí!\n\nNo puedo guardar la matriz de datos porque el archivo CSV está abierto en otro programa (seguramente Excel).\n\nArchivo: {csvPath}\n\nPor favor, cierra el Excel y pulsa 'Reintentar'.",
                        "Archivo Bloqueado por Excel",
                        MessageBoxButtons.RetryCancel,
                        MessageBoxIcon.Warning);

                    if (result == DialogResult.Cancel)
                    {
                        var confirm = MessageBox.Show("¿Estás seguro de cancelar? Perderás todo el trabajo de esta sesión de lote.", "Confirmar Cancelación", MessageBoxButtons.YesNo, MessageBoxIcon.Error);
                        if (confirm == DialogResult.Yes)
                        {
                            this.Close();
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ha ocurrido un error inesperado al intentar guardar el disco:\n{ex.Message}", "Error fatal", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    this.Close();
                    return;
                }
            }

            MessageBox.Show($"¡Proceso Completado con Éxito!\n\nSe han analizado todas las imágenes.\nLa matriz de datos se ha guardado de forma segura en:\n{csvPath}",
                "Fin del flujo", MessageBoxButtons.OK, MessageBoxIcon.Information);
            this.Close();
        }
    }
}