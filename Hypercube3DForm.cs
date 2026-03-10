using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SpecimenFX17.Imaging
{
    public class Hypercube3DForm : Form
    {
        private readonly HyperspectralCube _cube;
        private readonly IReadOnlyList<SelectionShape> _selections;
        private bool[,] _mask = null!;

        // Las 6 caras del cubo
        private Bitmap? _texFront, _texBack, _texTop, _texBottom, _texLeft, _texRight;
        private Color[] _wlColors = null!;

        private float _vmin = 0, _vmax = 1;

        // Cámara 3D Completa
        private float _yaw = (float)(-Math.PI / 4);   // Giro horizontal (360º)
        private float _pitch = (float)(Math.PI / 6);  // Inclinación vertical
        private float _zoom = 1.0f;
        private float _panX = 0, _panY = 0;

        // 6 Sliders para cortes completos
        private TrackBar _trkXMin = null!, _trkXMax = null!;
        private TrackBar _trkYMin = null!, _trkYMax = null!;
        private TrackBar _trkZMin = null!, _trkZMax = null!;

        private PictureBox _canvas = null!;

        private bool _isDragging = false;
        private bool _isPanning = false;
        private Point _lastMouse;

        // Controles asíncronos
        private bool _isRendering = false;
        private bool _needsRender = false;

        // Estructura vectorial matemática
        private struct Vector3
        {
            public float X, Y, Z;
            public Vector3(float x, float y, float z) { X = x; Y = y; Z = z; }
        }

        public Hypercube3DForm(HyperspectralCube cube, IReadOnlyList<SelectionShape> selections)
        {
            _cube = cube;
            _selections = selections;

            Text = "Visor Volumétrico 3D - 360º Orbital";
            Size = new Size(1200, 850);
            BackColor = Color.FromArgb(18, 18, 26);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9f);

            _mask = BuildMask();
            CalculateGlobalContrast();
            PrecomputeWavelengthColors();
            BuildUI();

            _zoom = Math.Min(400f / _cube.Samples, 400f / _cube.Lines);
            UpdateBitmapsAsync();
        }

        private bool[,] BuildMask()
        {
            var mask = new bool[_cube.Lines, _cube.Samples];
            if (_selections == null || _selections.Count == 0)
            {
                for (int y = 0; y < _cube.Lines; y++) for (int x = 0; x < _cube.Samples; x++) mask[y, x] = true;
                return mask;
            }

            foreach (var sh in _selections)
            {
                var m = sh.GetMask(_cube.Lines, _cube.Samples);
                for (int y = 0; y < _cube.Lines; y++) for (int x = 0; x < _cube.Samples; x++) if (m[y, x]) mask[y, x] = true;
            }
            return mask;
        }

        private void CalculateGlobalContrast()
        {
            var r = new Random(42);
            var samples = new List<float>();
            for (int i = 0; i < 5000; i++)
            {
                float v = _cube[r.Next(_cube.Bands), r.Next(_cube.Lines), r.Next(_cube.Samples)];
                if (!float.IsNaN(v)) samples.Add(v);
            }
            if (samples.Count > 0)
            {
                samples.Sort();
                _vmin = samples[(int)(samples.Count * 0.02)];
                _vmax = samples[(int)(samples.Count * 0.98)];
                if (_vmax - _vmin < 1e-6f) _vmax = _vmin + 1f;
            }
        }

        private void PrecomputeWavelengthColors()
        {
            _wlColors = new Color[_cube.Bands];
            for (int b = 0; b < _cube.Bands; b++)
            {
                float hue = 270f * (1f - (float)b / Math.Max(1, _cube.Bands - 1));
                _wlColors[b] = ColorFromHSL(hue, 1f, 0.5f);
            }
        }

        private void BuildUI()
        {
            var pnlLeft = new Panel { Dock = DockStyle.Left, Width = 300, BackColor = Color.FromArgb(24, 24, 34), Padding = new Padding(10) };

            int cy = 15;
            AddLabel(pnlLeft, "📐 CORTES ESPACIALES 3D", cy, true); cy += 30;

            void LinkTrackbars(TrackBar tMin, TrackBar tMax)
            {
                tMin.Scroll += (_, _) => { if (tMin.Value >= tMax.Value - 5) tMax.Value = Math.Min(tMax.Maximum, tMin.Value + 5); UpdateBitmapsAsync(); };
                tMax.Scroll += (_, _) => { if (tMax.Value <= tMin.Value + 5) tMin.Value = Math.Max(tMin.Minimum, tMax.Value - 5); UpdateBitmapsAsync(); };
            }

            // EJE X
            AddLabel(pnlLeft, "Corte X (Ancho) [Mín / Máx]:", cy); cy += 20;
            _trkXMin = new TrackBar { Location = new Point(5, cy), Width = 135, Minimum = 0, Maximum = _cube.Samples - 1, Value = 0, TickStyle = TickStyle.None };
            _trkXMax = new TrackBar { Location = new Point(145, cy), Width = 135, Minimum = 0, Maximum = _cube.Samples - 1, Value = _cube.Samples - 1, TickStyle = TickStyle.None };
            LinkTrackbars(_trkXMin, _trkXMax);
            pnlLeft.Controls.Add(_trkXMin); pnlLeft.Controls.Add(_trkXMax); cy += 40;

            // EJE Y
            AddLabel(pnlLeft, "Corte Y (Alto) [Mín / Máx]:", cy); cy += 20;
            _trkYMin = new TrackBar { Location = new Point(5, cy), Width = 135, Minimum = 0, Maximum = _cube.Lines - 1, Value = 0, TickStyle = TickStyle.None };
            _trkYMax = new TrackBar { Location = new Point(145, cy), Width = 135, Minimum = 0, Maximum = _cube.Lines - 1, Value = _cube.Lines - 1, TickStyle = TickStyle.None };
            LinkTrackbars(_trkYMin, _trkYMax);
            pnlLeft.Controls.Add(_trkYMin); pnlLeft.Controls.Add(_trkYMax); cy += 40;

            // EJE Z
            AddLabel(pnlLeft, "Corte Z (Bandas) [Mín / Máx]:", cy); cy += 20;
            _trkZMin = new TrackBar { Location = new Point(5, cy), Width = 135, Minimum = 0, Maximum = _cube.Bands - 1, Value = 0, TickStyle = TickStyle.None };
            _trkZMax = new TrackBar { Location = new Point(145, cy), Width = 135, Minimum = 0, Maximum = _cube.Bands - 1, Value = _cube.Bands - 1, TickStyle = TickStyle.None };
            LinkTrackbars(_trkZMin, _trkZMax);
            pnlLeft.Controls.Add(_trkZMin); pnlLeft.Controls.Add(_trkZMax); cy += 50;

            AddLabel(pnlLeft, "💡 Controles de Cámara Orbital:", cy, true); cy += 25;
            AddLabel(pnlLeft, "• Clic Izq + Arrastrar:\n   Rotación 360º Orbital\n\n• Clic Derecho + Arrastrar:\n   Mover la cámara (Pan)\n\n• Rueda del Ratón:\n   Acercar / Alejar zoom", cy);

            _canvas = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(10, 10, 16) };
            _canvas.Paint += Canvas_Paint;
            _canvas.MouseDown += (s, e) => {
                if (e.Button == MouseButtons.Left) _isDragging = true;
                if (e.Button == MouseButtons.Right) _isPanning = true;
                _lastMouse = e.Location;
            };
            _canvas.MouseUp += (s, e) => { _isDragging = false; _isPanning = false; };
            _canvas.MouseMove += (s, e) =>
            {
                float dx = e.X - _lastMouse.X;
                float dy = e.Y - _lastMouse.Y;

                if (_isDragging)
                {
                    _yaw -= dx * 0.01f;
                    _pitch -= dy * 0.01f;
                    _pitch = Math.Clamp(_pitch, (float)(-Math.PI / 2 + 0.05), (float)(Math.PI / 2 - 0.05)); // Limitar para no quedar bocarriba
                    _canvas.Invalidate();
                }
                else if (_isPanning)
                {
                    _panX += dx;
                    _panY += dy;
                    _canvas.Invalidate();
                }
                _lastMouse = e.Location;
            };
            _canvas.MouseWheel += (s, e) => { _zoom *= e.Delta > 0 ? 1.1f : 0.9f; _canvas.Invalidate(); };

            Controls.Add(_canvas);
            Controls.Add(pnlLeft);
        }

        private void AddLabel(Control p, string text, int y, bool title = false) => p.Controls.Add(new Label { Text = text, Location = new Point(10, y), AutoSize = true, ForeColor = title ? Color.LightSkyBlue : Color.LightGray, Font = new Font("Segoe UI", title ? 10f : 9f, title ? FontStyle.Bold : FontStyle.Regular) });

        private async void UpdateBitmapsAsync()
        {
            if (_isRendering) { _needsRender = true; return; }
            _isRendering = true; _needsRender = false;

            int x0 = _trkXMin.Value, x1 = _trkXMax.Value;
            int y0 = _trkYMin.Value, y1 = _trkYMax.Value;
            int z0 = _trkZMin.Value, z1 = _trkZMax.Value;

            try
            {
                var (bF, bB, bT, bBo, bL, bR) = await Task.Run(() => Build6Textures(x0, x1, y0, y1, z0, z1));

                var of = _texFront; var ob = _texBack; var ot = _texTop; var obo = _texBottom; var ol = _texLeft; var or = _texRight;
                _texFront = bF; _texBack = bB; _texTop = bT; _texBottom = bBo; _texLeft = bL; _texRight = bR;

                of?.Dispose(); ob?.Dispose(); ot?.Dispose(); obo?.Dispose(); ol?.Dispose(); or?.Dispose();
                _canvas.Invalidate();
            }
            catch (Exception ex) { Console.WriteLine($"Error 3D: {ex.Message}"); }
            finally
            {
                _isRendering = false;
                if (_needsRender) UpdateBitmapsAsync();
            }
        }

        private unsafe (Bitmap, Bitmap, Bitmap, Bitmap, Bitmap, Bitmap) Build6Textures(int x0, int x1, int y0, int y1, int z0, int z1)
        {
            float range = _vmax - _vmin; if (range <= 0) range = 1f;
            int cx = x1 - x0 + 1, cy = y1 - y0 + 1, cz = z1 - z0 + 1;

            var bF = new Bitmap(cx, cy, PixelFormat.Format32bppArgb);
            var bB = new Bitmap(cx, cy, PixelFormat.Format32bppArgb);
            var bT = new Bitmap(cx, cz, PixelFormat.Format32bppArgb);
            var bBo = new Bitmap(cx, cz, PixelFormat.Format32bppArgb);
            var bR = new Bitmap(cz, cy, PixelFormat.Format32bppArgb);
            var bL = new Bitmap(cz, cy, PixelFormat.Format32bppArgb);

            // 1. FRONT
            var bdF = bF.LockBits(new Rectangle(0, 0, cx, cy), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Parallel.For(0, cy, y => {
                byte* row = (byte*)bdF.Scan0 + y * bdF.Stride;
                for (int x = 0; x < cx; x++)
                {
                    if (_mask[y + y0, x + x0])
                    {
                        float t = Math.Clamp((_cube[z1, y + y0, x + x0] - _vmin) / range, 0, 1);
                        var c = GetRainbowColor(t);
                        row[x * 4] = c.B; row[x * 4 + 1] = c.G; row[x * 4 + 2] = c.R; row[x * 4 + 3] = 255;
                    }
                    else { row[x * 4 + 3] = 0; }
                }
            });
            bF.UnlockBits(bdF);

            // 2. BACK (Proyectado del revés automáticamente por la matemática del cubo)
            var bdB = bB.LockBits(new Rectangle(0, 0, cx, cy), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Parallel.For(0, cy, y => {
                byte* row = (byte*)bdB.Scan0 + y * bdB.Stride;
                for (int x = 0; x < cx; x++)
                {
                    if (_mask[y + y0, x + x0])
                    {
                        float t = Math.Clamp((_cube[z0, y + y0, x + x0] - _vmin) / range, 0, 1);
                        var c = GetRainbowColor(t);
                        row[x * 4] = c.B; row[x * 4 + 1] = c.G; row[x * 4 + 2] = c.R; row[x * 4 + 3] = 255;
                    }
                    else { row[x * 4 + 3] = 0; }
                }
            });
            bB.UnlockBits(bdB);

            // 3. TOP
            var bdT = bT.LockBits(new Rectangle(0, 0, cx, cz), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Parallel.For(0, cz, y => {
                int actualZ = z0 + y; Color wlCol = _wlColors[actualZ];
                byte* row = (byte*)bdT.Scan0 + y * bdT.Stride;
                for (int x = 0; x < cx; x++)
                {
                    int eY = -1; for (int iy = y0; iy <= y1; iy++) if (_mask[iy, x + x0]) { eY = iy; break; }
                    if (eY != -1)
                    {
                        float t = Math.Clamp((_cube[actualZ, eY, x + x0] - _vmin) / range, 0, 1);
                        float i = 0.25f + 0.75f * t;
                        row[x * 4] = (byte)(wlCol.B * i); row[x * 4 + 1] = (byte)(wlCol.G * i); row[x * 4 + 2] = (byte)(wlCol.R * i); row[x * 4 + 3] = 240;
                    }
                    else { row[x * 4 + 3] = 0; }
                }
            });
            bT.UnlockBits(bdT);

            // 4. BOTTOM
            var bdBo = bBo.LockBits(new Rectangle(0, 0, cx, cz), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Parallel.For(0, cz, y => {
                int actualZ = z1 - y; Color wlCol = _wlColors[actualZ];
                byte* row = (byte*)bdBo.Scan0 + y * bdBo.Stride;
                for (int x = 0; x < cx; x++)
                {
                    int eY = -1; for (int iy = y1; iy >= y0; iy--) if (_mask[iy, x + x0]) { eY = iy; break; }
                    if (eY != -1)
                    {
                        float t = Math.Clamp((_cube[actualZ, eY, x + x0] - _vmin) / range, 0, 1);
                        float i = 0.25f + 0.75f * t;
                        row[x * 4] = (byte)(wlCol.B * i); row[x * 4 + 1] = (byte)(wlCol.G * i); row[x * 4 + 2] = (byte)(wlCol.R * i); row[x * 4 + 3] = 240;
                    }
                    else { row[x * 4 + 3] = 0; }
                }
            });
            bBo.UnlockBits(bdBo);

            // 5. RIGHT
            var bdR = bR.LockBits(new Rectangle(0, 0, cz, cy), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Parallel.For(0, cy, y => {
                byte* row = (byte*)bdR.Scan0 + y * bdR.Stride;
                for (int x = 0; x < cz; x++)
                {
                    int actualZ = z1 - x; Color wlCol = _wlColors[actualZ];
                    int eX = -1; for (int ix = x1; ix >= x0; ix--) if (_mask[y + y0, ix]) { eX = ix; break; }
                    if (eX != -1)
                    {
                        float t = Math.Clamp((_cube[actualZ, y + y0, eX] - _vmin) / range, 0, 1);
                        float i = 0.25f + 0.75f * t;
                        row[x * 4] = (byte)(wlCol.B * i); row[x * 4 + 1] = (byte)(wlCol.G * i); row[x * 4 + 2] = (byte)(wlCol.R * i); row[x * 4 + 3] = 240;
                    }
                    else { row[x * 4 + 3] = 0; }
                }
            });
            bR.UnlockBits(bdR);

            // 6. LEFT
            var bdL = bL.LockBits(new Rectangle(0, 0, cz, cy), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Parallel.For(0, cy, y => {
                byte* row = (byte*)bdL.Scan0 + y * bdL.Stride;
                for (int x = 0; x < cz; x++)
                {
                    int actualZ = z0 + x; Color wlCol = _wlColors[actualZ];
                    int eX = -1; for (int ix = x0; ix <= x1; ix++) if (_mask[y + y0, ix]) { eX = ix; break; }
                    if (eX != -1)
                    {
                        float t = Math.Clamp((_cube[actualZ, y + y0, eX] - _vmin) / range, 0, 1);
                        float i = 0.25f + 0.75f * t;
                        row[x * 4] = (byte)(wlCol.B * i); row[x * 4 + 1] = (byte)(wlCol.G * i); row[x * 4 + 2] = (byte)(wlCol.R * i); row[x * 4 + 3] = 240;
                    }
                    else { row[x * 4 + 3] = 0; }
                }
            });
            bL.UnlockBits(bdL);

            return (bF, bB, bT, bBo, bL, bR);
        }

        private Vector3 Rotate(Vector3 v)
        {
            // Pitch (Rotación X)
            float cosP = (float)Math.Cos(_pitch), sinP = (float)Math.Sin(_pitch);
            float y1 = v.Y * cosP - v.Z * sinP;
            float z1 = v.Y * sinP + v.Z * cosP;

            // Yaw (Rotación Y)
            float cosY = (float)Math.Cos(_yaw), sinY = (float)Math.Sin(_yaw);
            float x2 = v.X * cosY + z1 * sinY;
            float z2 = -v.X * sinY + z1 * cosY;

            return new Vector3(x2 * _zoom, y1 * _zoom, z2 * _zoom);
        }

        private void Canvas_Paint(object? sender, PaintEventArgs e)
        {
            if (_texFront == null) return;

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;

            float W = _trkXMax.Value - _trkXMin.Value + 1;
            float H = _trkYMax.Value - _trkYMin.Value + 1;
            float D = _trkZMax.Value - _trkZMin.Value + 1;

            // 8 Vértices del cubo centrados en 0,0,0
            Vector3[] V = new Vector3[8];
            V[0] = new Vector3(-W / 2, -H / 2, -D / 2); // Top-Left-Front
            V[1] = new Vector3(W / 2, -H / 2, -D / 2); // Top-Right-Front
            V[2] = new Vector3(W / 2, H / 2, -D / 2); // Bottom-Right-Front
            V[3] = new Vector3(-W / 2, H / 2, -D / 2); // Bottom-Left-Front
            V[4] = new Vector3(-W / 2, -H / 2, D / 2); // Top-Left-Back
            V[5] = new Vector3(W / 2, -H / 2, D / 2); // Top-Right-Back
            V[6] = new Vector3(W / 2, H / 2, D / 2); // Bottom-Right-Back
            V[7] = new Vector3(-W / 2, H / 2, D / 2); // Bottom-Left-Back

            Vector3[] pV = new Vector3[8];
            for (int i = 0; i < 8; i++) pV[i] = Rotate(V[i]);

            float oX = _canvas.Width / 2f + _panX;
            float oY = _canvas.Height / 2f + _panY;

            PointF P(int idx) => new PointF(oX + pV[idx].X, oY + pV[idx].Y);

            // Definición de las 6 caras (Textura, 4 Vértices, Normal)
            var faces = new[] {
                (Tex: _texFront,  Pts: new[]{0,1,2,3}, Norm: new Vector3(0,0,-1)),
                (Tex: _texBack,   Pts: new[]{5,4,7,6}, Norm: new Vector3(0,0,1)),
                (Tex: _texTop,    Pts: new[]{4,5,1,0}, Norm: new Vector3(0,-1,0)),
                (Tex: _texBottom, Pts: new[]{3,2,6,7}, Norm: new Vector3(0,1,0)),
                (Tex: _texRight,  Pts: new[]{1,5,6,2}, Norm: new Vector3(1,0,0)),
                (Tex: _texLeft,   Pts: new[]{4,0,3,7}, Norm: new Vector3(-1,0,0))
            };

            var toDraw = new List<(Bitmap Tex, int[] Pts, float Dist)>();

            foreach (var f in faces)
            {
                // Backface culling rotando la normal
                var n = Rotate(f.Norm);
                if (n.Z < 0)
                {
                    // Centro de la cara para ordenar por profundidad (Painter's Algorithm)
                    Vector3 center = new Vector3(
                        (V[f.Pts[0]].X + V[f.Pts[2]].X) / 2,
                        (V[f.Pts[0]].Y + V[f.Pts[2]].Y) / 2,
                        (V[f.Pts[0]].Z + V[f.Pts[2]].Z) / 2);

                    toDraw.Add((f.Tex, f.Pts, Rotate(center).Z));
                }
            }

            // Ordenar las caras: Las más alejadas se dibujan primero
            toDraw.Sort((a, b) => b.Dist.CompareTo(a.Dist));

            using var edgePen = new Pen(Color.FromArgb(90, 255, 255, 255), 1.5f) { LineJoin = LineJoin.Round };

            try
            {
                foreach (var f in toDraw)
                {
                    // DrawImage mapea (TopLeft, TopRight, BottomLeft)
                    PointF[] imgPts = { P(f.Pts[0]), P(f.Pts[1]), P(f.Pts[3]) };
                    g.DrawImage(f.Tex, imgPts);

                    // Dibujar el marco de cristal para resaltar la volumetría
                    g.DrawPolygon(edgePen, new[] { P(f.Pts[0]), P(f.Pts[1]), P(f.Pts[2]), P(f.Pts[3]) });
                }
            }
            catch { /* Ignorar errores GDI+ al minimizar a 0 px */ }
        }

        private static (byte R, byte G, byte B) GetRainbowColor(float t)
        {
            float r = 0, g = 0, b = 0;
            if (t < 0.125f) { r = 0; g = 0; b = 0.5f + t * 4f; }
            else if (t < 0.375f) { r = 0; g = (t - .125f) * 4f; b = 1f; }
            else if (t < 0.625f) { r = (t - .375f) * 4f; g = 1f; b = 1f - (t - .375f) * 4f; }
            else if (t < 0.875f) { r = 1f; g = 1f - (t - .625f) * 4f; b = 0f; }
            else { r = 1f; g = (t - .875f) * 8f; b = (t - .875f) * 8f; }
            return ((byte)Math.Clamp(r * 255, 0, 255), (byte)Math.Clamp(g * 255, 0, 255), (byte)Math.Clamp(b * 255, 0, 255));
        }

        private static Color ColorFromHSL(float h, float s, float l)
        {
            float c = (1 - Math.Abs(2 * l - 1)) * s;
            float x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            float m = l - c / 2;
            float r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            return Color.FromArgb((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
        }
    }
}