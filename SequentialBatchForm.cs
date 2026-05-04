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

        // Controles UI
        private PictureBox _picView = null!;
        private Label _lblInfo = null!;
        private TextBox _txtClass = null!;
        private Button _btnNext = null!;
        private ProgressBar _pbProgress = null!;
        private StringBuilder _csvMatrix = new();

        public SequentialBatchForm(string inputFolder, string outputFolder, BatchOptions opts)
        {
            _outputPath = outputFolder;
            _opts = opts;
            _files = Directory.GetFiles(inputFolder, "*.hdr").OrderBy(f => f).ToList();

            Text = "Asistente de Segmentación Secuencial";
            Size = new System.Drawing.Size(1200, 850);
            BackColor = Color.FromArgb(30, 30, 35);
            ForeColor = Color.White;

            BuildUI();
            InitializeCsv();
            LoadNextImage();
        }

        private void InitializeCsv()
        {
            // La cabecera se creará con la primera imagen para saber cuántas bandas hay
        }

        private void BuildUI()
        {
            var pnlTop = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Color.FromArgb(20, 20, 25), Padding = new Padding(10) };
            _lblInfo = new Label { Text = "Cargando...", AutoSize = true, Font = new Font("Segoe UI", 12f, FontStyle.Bold), ForeColor = Color.LightSkyBlue };
            _pbProgress = new ProgressBar { Dock = DockStyle.Bottom, Height = 5 };
            pnlTop.Controls.Add(_lblInfo);
            pnlTop.Controls.Add(_pbProgress);

            var pnlRight = new Panel { Dock = DockStyle.Right, Width = 300, BackColor = Color.FromArgb(25, 25, 30), Padding = new Padding(15) };

            var lblClass = new Label { Text = "Etiqueta / Clase de esta imagen:", Dock = DockStyle.Top, Height = 25 };
            _txtClass = new TextBox { Dock = DockStyle.Top, BackColor = Color.FromArgb(45, 45, 50), ForeColor = Color.White, Font = new Font("Segoe UI", 11f) };

            var lblHint = new Label
            {
                Text = "\n💡 INSTRUCCIONES:\n\n1. Los objetos detectados tienen un NÚMERO.\n2. CLIC DERECHO sobre un objeto para ELIMINARLO si es un error.\n3. Ajusta la Clase y pulsa SIGUIENTE.",
                Dock = DockStyle.Top,
                Height = 200,
                ForeColor = Color.Gray,
                Font = new Font("Segoe UI", 9f, FontStyle.Italic)
            };

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

            pnlRight.Controls.Add(lblHint);
            pnlRight.Controls.Add(_txtClass);
            pnlRight.Controls.Add(lblClass);
            pnlRight.Controls.Add(_btnNext);

            _picView = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black, Cursor = Cursors.Hand };
            _picView.MouseDown += PicView_MouseDown;
            _picView.Paint += PicView_Paint;

            Controls.Add(_picView);
            Controls.Add(pnlRight);
            Controls.Add(pnlTop);
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
            _lblInfo.Text = $"Procesando {_currentIndex + 1} de {_files.Count}: {Path.GetFileName(path)}";
            _pbProgress.Value = (_currentIndex * 100) / _files.Count;
            _btnNext.Enabled = false;

            try
            {
                await Task.Run(() => {
                    _currentCube?.Dispose();
                    _currentCube = HyperspectralCube.Load(path);

                    // Aplicar calibración si está en las opciones (aquí deberías pasar las refs de MainForm si quieres)
                    // _currentCube.Calibrate(...);
                });

                // Segmentación Automática
                _currentRois = await AutoSegmenter.SegmentCubeAsync(_currentCube!, _opts.SegmentationBand, _opts.CustomParams);

                // Extraer el nombre de la imagen como clase por defecto
                _txtClass.Text = Path.GetFileNameWithoutExtension(path).Split('_')[0];

                RefreshView();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error cargando imagen: {ex.Message}");
            }
            finally
            {
                _btnNext.Enabled = true;
            }
        }

        private void RefreshView()
        {
            if (_currentCube == null) return;

            // Renderizamos la banda para previsualizar
            var renderOpts = new BliRenderOptions { Colormap = BliColormap.Grayscale, DrawColorbar = false };
            var bmp = BliRenderer.RenderBand(_currentCube, _opts.SegmentationBand, renderOpts);

            _picView.Image?.Dispose();
            _picView.Image = bmp;
        }

        private void PicView_Paint(object? sender, PaintEventArgs e)
        {
            if (_picView.Image == null) return;

            // Dibujar ROIs y números
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Necesitamos calcular la escala del zoom del PictureBox
            float ratio = Math.Min((float)_picView.Width / _currentCube!.Samples, (float)_picView.Height / _currentCube!.Lines);
            float offX = (_picView.Width - _currentCube.Samples * ratio) / 2;
            float offY = (_picView.Height - _currentCube.Lines * ratio) / 2;

            int count = 1;
            foreach (var roi in _currentRois)
            {
                // Dibujar contorno
                using var p = new Pen(roi.Color, 2);
                // Aquí simplificamos dibujando un rectángulo alrededor de la máscara
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

                // Dibujar número gigante
                string txt = count.ToString();
                using var font = new Font("Arial", 14, FontStyle.Bold);
                var sz = g.MeasureString(txt, font);
                g.FillRectangle(new SolidBrush(roi.Color), rect.X, rect.Y - sz.Height, sz.Width, sz.Height);
                g.DrawString(txt, font, Brushes.Black, rect.X, rect.Y - sz.Height);

                count++;
            }
        }

        private void PicView_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                // Mapear clic a imagen
                float ratio = Math.Min((float)_picView.Width / _currentCube!.Samples, (float)_picView.Height / _currentCube!.Lines);
                float offX = (_picView.Width - _currentCube.Samples * ratio) / 2;
                float offY = (_picView.Height - _currentCube.Lines * ratio) / 2;

                int imgX = (int)((e.X - offX) / ratio);
                int imgY = (int)((e.Y - offY) / ratio);

                // Buscar qué ROI contiene ese punto y borrarla
                var toRemove = _currentRois.FirstOrDefault(r => r.Contains(new System.Drawing.Point(imgX, imgY)));
                if (toRemove != null)
                {
                    _currentRois.Remove(toRemove);
                    _picView.Invalidate();
                }
            }
        }

        private void ProcessAndNext()
        {
            if (_currentCube == null) return;

            // 1. Si es la primera imagen, inicializar cabecera del CSV
            if (_csvMatrix.Length == 0)
            {
                _csvMatrix.Append("ID_Muestra,Clase,Nombre_Archivo");
                for (int i = 0; i < _currentCube.Bands; i++)
                {
                    double wl = _currentCube.Header.Wavelengths.Count > i ? _currentCube.Header.Wavelengths[i] : i;
                    _csvMatrix.Append($",{wl:F2}");
                }
                _csvMatrix.AppendLine();
            }

            // 2. Calcular espectro medio de cada ROI válida y añadir a la matriz
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
            File.WriteAllText(csvPath, _csvMatrix.ToString(), Encoding.UTF8);

            MessageBox.Show($"¡Proceso Completado!\n\nSe han analizado todas las imágenes.\nLa matriz de datos se ha guardado en:\n{csvPath}",
                "Fin del flujo", MessageBoxButtons.OK, MessageBoxIcon.Information);
            this.Close();
        }
    }
}