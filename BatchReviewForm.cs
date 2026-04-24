using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SpecimenFX17.Imaging
{
    public class BatchReviewForm : WeifenLuo.WinFormsUI.Docking.DockContent
    {
        private FlowLayoutPanel _galleryPanel = null!;
        private ProgressBar _pb = null!;
        private Label _lblStatus = null!;
        private Button _btnSendToAnalysis = null!;
        private CancellationTokenSource? _cts;
        private readonly string _folderPath;

        // Almacenamos los archivos originales y cuáles han sido seleccionados por el usuario
        private readonly List<string> _loadedFiles = new();
        private readonly HashSet<string> _selectedFiles = new();

        // Evento para comunicar a MainForm que hemos creado un nuevo Collage
        public event EventHandler<HyperspectralCube>? OnCollageCreated;

        public BatchReviewForm(string folderPath)
        {
            _folderPath = folderPath;
            Text = $"Galería de Análisis: {Path.GetFileName(folderPath)}";
            Size = new Size(1100, 750);
            BackColor = Color.FromArgb(30, 30, 30);
            ForeColor = Color.White;

            BuildUI();
            LoadGalleryAsync();
        }

        private void BuildUI()
        {
            var pnlTop = new Panel { Dock = DockStyle.Top, Height = 55, BackColor = Color.FromArgb(22, 22, 34), Padding = new Padding(10) };

            _lblStatus = new Label { AutoSize = true, ForeColor = Color.LightSkyBlue, Font = new Font("Segoe UI", 9f, FontStyle.Bold), Text = "Iniciando carga...", Dock = DockStyle.Left, Padding = new Padding(0, 5, 0, 0) };
            _pb = new ProgressBar { Style = ProgressBarStyle.Continuous, Width = 300, Dock = DockStyle.Left, Visible = false, Margin = new Padding(20, 5, 20, 5) };

            // NUEVO BOTÓN: Mandar a Análisis (Collage)
            _btnSendToAnalysis = new Button
            {
                Text = "🚀 Mandar a Análisis (Crear Collage)",
                Dock = DockStyle.Right,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(40, 110, 60),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                AutoSize = true,
                Padding = new Padding(15, 0, 15, 0),
                Enabled = false // Se habilita cuando seleccionas imágenes
            };
            _btnSendToAnalysis.FlatAppearance.BorderColor = Color.FromArgb(80, 150, 100);
            _btnSendToAnalysis.Click += BtnSendToAnalysis_Click;

            var btnCancel = new Button { Text = "🛑 Cancelar", Dock = DockStyle.Right, FlatStyle = FlatStyle.Flat, ForeColor = Color.LightCoral, BackColor = Color.FromArgb(50, 20, 20), Margin = new Padding(0, 0, 15, 0) };
            btnCancel.FlatAppearance.BorderSize = 0;
            btnCancel.Click += (s, e) => _cts?.Cancel();

            pnlTop.Controls.Add(_lblStatus);
            pnlTop.Controls.Add(_pb);
            pnlTop.Controls.Add(_btnSendToAnalysis);
            pnlTop.Controls.Add(btnCancel);

            _galleryPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(18, 18, 26),
                Padding = new Padding(15)
            };

            Controls.Add(_galleryPanel);
            Controls.Add(pnlTop);
        }

        private async void LoadGalleryAsync()
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            _pb.Visible = true;
            _pb.Value = 0;

            try
            {
                var files = Directory.GetFiles(_folderPath, "*.hdr").ToList();
                if (files.Count == 0)
                {
                    _lblStatus.Text = "No se encontraron archivos .hdr en la carpeta.";
                    return;
                }

                _lblStatus.Text = $"Cargando {files.Count} imágenes...";

                for (int i = 0; i < files.Count; i++)
                {
                    token.ThrowIfCancellationRequested();

                    string hdrPath = files[i];
                    string fileName = Path.GetFileNameWithoutExtension(hdrPath);

                    Bitmap? thumb = await Task.Run(() => ExtractThumbnail(hdrPath), token);

                    if (thumb != null)
                    {
                        _loadedFiles.Add(hdrPath);
                        AddGalleryCard(hdrPath, fileName, thumb);
                    }

                    _pb.Value = (i + 1) * 100 / files.Count;
                    _lblStatus.Text = $"Cargadas {i + 1} de {files.Count} imágenes...";
                }

                _lblStatus.Text = $"Galería completada ({_loadedFiles.Count} imágenes válidas). Selecciona varias y pulsa Mandar a Análisis.";
            }
            catch (OperationCanceledException) { _lblStatus.Text = "Carga de galería cancelada."; }
            catch (Exception ex) { MessageBox.Show($"Error cargando la galería: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { _pb.Visible = false; }
        }

        private Bitmap? ExtractThumbnail(string hdrPath)
        {
            try
            {
                using var cube = HyperspectralCube.Load(hdrPath);
                int renderBand = cube.Bands / 2;
                var opts = new BliRenderOptions { Colormap = BliColormap.Grayscale, DrawColorbar = false };

                using var fullBmp = BliRenderer.RenderBand(cube, renderBand, opts);

                int thumbWidth = 200;
                int thumbHeight = (int)((float)fullBmp.Height / fullBmp.Width * thumbWidth);

                var thumbBmp = new Bitmap(thumbWidth, thumbHeight);
                using var g = Graphics.FromImage(thumbBmp);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(fullBmp, 0, 0, thumbWidth, thumbHeight);

                return thumbBmp;
            }
            catch { return null; }
        }

        private void AddGalleryCard(string filePath, string title, Bitmap image)
        {
            var card = new Panel { Width = 210, Height = 250, Margin = new Padding(10), BackColor = Color.FromArgb(36, 36, 46), Cursor = Cursors.Hand };
            var pic = new PictureBox { Image = image, SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Top, Height = 200, BackColor = Color.Black };
            var lbl = new Label { Text = title, Dock = DockStyle.Bottom, Height = 50, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.LightGray, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) };

            void ToggleSelection(object? sender, EventArgs e)
            {
                if (_selectedFiles.Contains(filePath))
                {
                    _selectedFiles.Remove(filePath);
                    card.BackColor = Color.FromArgb(36, 36, 46); // Deseleccionado (Normal)
                    lbl.ForeColor = Color.LightGray;
                }
                else
                {
                    _selectedFiles.Add(filePath);
                    card.BackColor = Color.FromArgb(0, 122, 204); // Seleccionado (Azul Specimen)
                    lbl.ForeColor = Color.White;
                }

                _btnSendToAnalysis.Enabled = _selectedFiles.Count > 0;
                _btnSendToAnalysis.Text = $"🚀 Mandar a Análisis ({_selectedFiles.Count} Collage)";
            }

            card.Click += ToggleSelection;
            pic.Click += ToggleSelection;
            lbl.Click += ToggleSelection;

            card.Controls.Add(pic);
            card.Controls.Add(lbl);
            _galleryPanel.Controls.Add(card);
        }

        // --- MOTOR MATEMÁTICO: FUSIÓN DE HIPERCUBOS (COLLAGE) ---
        private async void BtnSendToAnalysis_Click(object? sender, EventArgs e)
        {
            if (_selectedFiles.Count == 0) return;

            _btnSendToAnalysis.Enabled = false;
            _lblStatus.Text = "Fusionando cubos hiperespectrales... (Esto consumirá bastante RAM)";
            _pb.Visible = true;
            _pb.Style = ProgressBarStyle.Marquee;

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            try
            {
                // Ordenamos la selección para mantener coherencia
                var filesToStitch = _selectedFiles.OrderBy(f => f).ToList();
                HyperspectralCube collageCube = await Task.Run(() => StitchCubes(filesToStitch, token), token);

                // Disparamos el evento para que MainForm reciba el cubo y cerramos la galería
                OnCollageCreated?.Invoke(this, collageCube);
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creando el collage:\n\n{ex.Message}\n\nNota: Asegúrate de que todas las imágenes tienen el mismo número de bandas.", "Fallo de Fusión", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _btnSendToAnalysis.Enabled = true;
            }
            finally
            {
                _pb.Visible = false;
                _pb.Style = ProgressBarStyle.Continuous;
            }
        }

        private HyperspectralCube StitchCubes(List<string> filePaths, CancellationToken ct)
        {
            // 1. Cargar todas las cabeceras primero para calcular dimensiones
            var headers = new List<EnviHeader>();
            int totalWidth = 0;
            int maxHeight = 0;
            int bands = -1;

            foreach (var path in filePaths)
            {
                ct.ThrowIfCancellationRequested();
                var h = EnviHeader.Load(path);
                headers.Add(h);

                if (bands == -1) bands = h.Bands;
                else if (h.Bands != bands) throw new Exception($"El archivo {Path.GetFileName(path)} tiene {h.Bands} bandas, pero se esperaban {bands}. No se pueden mezclar sensores diferentes.");

                totalWidth += h.Samples; // Se pondrán una al lado de la otra (Horizontal Collage)
                if (h.Lines > maxHeight) maxHeight = h.Lines;
            }

            // 2. Crear un nuevo almacenamiento mapeado gigante para el collage
            var collageStorage = new TempMappedStorage(bands, maxHeight, totalWidth);
            var po = new ParallelOptions { CancellationToken = ct };

            // Rellenar de 0 (Fondo negro) por si las imágenes tienen distintas alturas
            Parallel.For(0, bands, po, b => {
                for (int y = 0; y < maxHeight; y++)
                    for (int x = 0; x < totalWidth; x++)
                        collageStorage.Set(b, y, x, 0f);
            });

            // 3. Volcar los datos de cada cubo en su posición horizontal correspondiente
            int currentXOffset = 0;

            foreach (var path in filePaths)
            {
                ct.ThrowIfCancellationRequested();
                using var sourceCube = HyperspectralCube.Load(path);

                int sWidth = sourceCube.Samples;
                int sHeight = sourceCube.Lines;

                Parallel.For(0, bands, po, b =>
                {
                    for (int y = 0; y < sHeight; y++)
                    {
                        for (int x = 0; x < sWidth; x++)
                        {
                            // Copiamos el píxel del cubo origen al cubo gigante desplazado en X
                            collageStorage.Set(b, y, currentXOffset + x, sourceCube[b, y, x]);
                        }
                    }
                });

                currentXOffset += sWidth; // Desplazamos el offset para la siguiente imagen
            }

            // 4. Crear una nueva cabecera ENVI clonada de la primera pero con las nuevas dimensiones
            var firstHeader = headers[0];

            // Truco: Guardamos un archivo .hdr temporal en disco con las dimensiones gigantes para que la clase HyperspectralCube pueda leerlo
            string tempHdrPath = Path.Combine(Path.GetTempPath(), $"SPECIMEN_Collage_{Guid.NewGuid()}.hdr");

            using (var sw = new StreamWriter(tempHdrPath))
            {
                sw.WriteLine("ENVI");
                sw.WriteLine("description = { Collage Generado en SpecimenFX17 }");
                sw.WriteLine($"samples = {totalWidth}");
                sw.WriteLine($"lines = {maxHeight}");
                sw.WriteLine($"bands = {bands}");
                sw.WriteLine("header offset = 0");
                sw.WriteLine("file type = ENVI Standard");
                sw.WriteLine("data type = 4");
                sw.WriteLine("interleave = bil");
                sw.WriteLine("sensor type = Unknown");
                sw.WriteLine("byte order = 0");
                if (firstHeader.Wavelengths.Count > 0)
                {
                    sw.WriteLine("wavelength = {");
                    sw.WriteLine(string.Join(", ", firstHeader.Wavelengths.Select(w => w.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))));
                    sw.WriteLine("}");
                }
            }

            var finalHeader = EnviHeader.Load(tempHdrPath);
            return new HyperspectralCube(finalHeader, collageStorage);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _cts?.Cancel();
            base.OnFormClosing(e);
        }
    }
}