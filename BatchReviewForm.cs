using System;
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
        private CancellationTokenSource? _cts;
        private readonly string _folderPath;

        public BatchReviewForm(string folderPath)
        {
            _folderPath = folderPath;
            Text = $"Galería de Análisis: {Path.GetFileName(folderPath)}";
            Size = new Size(1000, 700);
            BackColor = Color.FromArgb(30, 30, 30);
            ForeColor = Color.White;

            BuildUI();
            LoadGalleryAsync(); // Iniciamos la carga al abrir
        }

        private void BuildUI()
        {
            var pnlTop = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Color.FromArgb(22, 22, 34), Padding = new Padding(10) };

            _lblStatus = new Label { AutoSize = true, ForeColor = Color.LightSkyBlue, Font = new Font("Segoe UI", 9f, FontStyle.Bold), Text = "Iniciando carga...", Dock = DockStyle.Left };
            _pb = new ProgressBar { Style = ProgressBarStyle.Continuous, Width = 300, Dock = DockStyle.Right, Visible = false };

            var btnCancel = new Button { Text = "🛑 Cancelar", Dock = DockStyle.Right, FlatStyle = FlatStyle.Flat, ForeColor = Color.LightCoral, BackColor = Color.FromArgb(50, 20, 20) };
            btnCancel.FlatAppearance.BorderSize = 0;
            btnCancel.Click += (s, e) => _cts?.Cancel();

            pnlTop.Controls.Add(_lblStatus);
            pnlTop.Controls.Add(_pb);
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

                    // Extraemos la miniatura asíncronamente
                    Bitmap? thumb = await Task.Run(() => ExtractThumbnail(hdrPath), token);

                    if (thumb != null)
                    {
                        // Añadimos la tarjeta al Collage en el hilo de la UI
                        AddGalleryCard(fileName, thumb);
                    }

                    _pb.Value = (i + 1) * 100 / files.Count;
                    _lblStatus.Text = $"Cargadas {i + 1} de {files.Count} imágenes...";
                }

                _lblStatus.Text = $"Galería completada ({files.Count} imágenes).";
            }
            catch (OperationCanceledException)
            {
                _lblStatus.Text = "Carga de galería cancelada.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error cargando la galería: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _pb.Visible = false;
            }
        }

        private Bitmap? ExtractThumbnail(string hdrPath)
        {
            try
            {
                // 1. Cargamos el cubo
                using var cube = HyperspectralCube.Load(hdrPath);

                // 2. Renderizamos la banda central en escala de grises (o la 0 si está segmentada)
                int renderBand = cube.Bands / 2;
                var opts = new BliRenderOptions { Colormap = BliColormap.Grayscale, DrawColorbar = false };

                using var fullBmp = BliRenderer.RenderBand(cube, renderBand, opts);

                // 3. Redimensionamos a un Thumbnail ligero (ej. 200px de ancho) para ahorrar RAM
                int thumbWidth = 200;
                int thumbHeight = (int)((float)fullBmp.Height / fullBmp.Width * thumbWidth);

                var thumbBmp = new Bitmap(thumbWidth, thumbHeight);
                using var g = Graphics.FromImage(thumbBmp);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(fullBmp, 0, 0, thumbWidth, thumbHeight);

                return thumbBmp;
                // Al salir del método, el bloque 'using' destruye el hipercubo masivo de la RAM
            }
            catch
            {
                return null; // Si falla un archivo, simplemente lo saltamos
            }
        }

        private void AddGalleryCard(string title, Bitmap image)
        {
            // Contenedor de la tarjeta
            var card = new Panel
            {
                Width = 210,
                Height = 250,
                Margin = new Padding(10),
                BackColor = Color.FromArgb(36, 36, 46),
                Cursor = Cursors.Hand
            };

            var pic = new PictureBox
            {
                Image = image,
                SizeMode = PictureBoxSizeMode.Zoom,
                Dock = DockStyle.Top,
                Height = 200,
                BackColor = Color.Black
            };

            var lbl = new Label
            {
                Text = title,
                Dock = DockStyle.Bottom,
                Height = 50,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.LightGray,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
            };

            // Efecto Hover
            card.MouseEnter += (s, e) => card.BackColor = Color.FromArgb(60, 60, 80);
            card.MouseLeave += (s, e) => card.BackColor = Color.FromArgb(36, 36, 46);
            pic.MouseEnter += (s, e) => card.BackColor = Color.FromArgb(60, 60, 80);
            pic.MouseLeave += (s, e) => card.BackColor = Color.FromArgb(36, 36, 46);

            // Evento Click: Aquí podrías hacer que se cargue la imagen en el visor principal
            EventHandler onClick = (s, e) =>
            {
                MessageBox.Show($"Has hecho clic en: {title}\n(Aquí se puede programar que esta imagen se cargue en el visor principal de SPECIMEN)", "Información");
            };

            card.Click += onClick;
            pic.Click += onClick;
            lbl.Click += onClick;

            card.Controls.Add(pic);
            card.Controls.Add(lbl);

            _galleryPanel.Controls.Add(card);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _cts?.Cancel(); // Cancelar la carga si se cierra la ventana antes de terminar
            base.OnFormClosing(e);
        }
    }
}