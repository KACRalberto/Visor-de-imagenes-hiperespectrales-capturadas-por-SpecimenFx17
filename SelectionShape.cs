using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;

namespace SpecimenFX17.Imaging
{
    public enum SelectionTool { Rectangle, Polygon, Circle, Freehand, AutoDetect, Point }

    public abstract class SelectionShape
    {
        public Color Color { get; set; }
        public abstract string ShortLabel { get; }
        public abstract string LegendIcon { get; }

        public string Variety { get; set; } = "";
        public string Date { get; set; } = DateTime.Now.ToString("yyyy-MM-dd");
        public float? MeasuredBrix { get; set; } = null;
        public string Notes { get; set; } = "";

        // ── CACHÉ DE RENDIMIENTO PARA EVITAR LAG EN EL MOUSEMOVE ──
        protected float[]? _cachedSpectrum;
        protected Guid _cachedVersion;

        protected SelectionShape(Color c) => Color = c;

        public abstract bool[,] GetMask(int lines, int samples);
        public abstract void DrawOn(Graphics g);
        public abstract bool Contains(Point pt);

        // Método para invalidar la caché cuando la máscara cambia dinámicamente
        public void InvalidateCache()
        {
            _cachedSpectrum = null;
        }

        public virtual float[] GetSpectrum(HyperspectralCube cube)
        {
            if (_cachedSpectrum != null && _cachedVersion == cube.Version)
                return _cachedSpectrum;

            var mask = GetMask(cube.Lines, cube.Samples);
            var res = new double[cube.Bands];
            int count = 0;
            object sync = new object();

            Parallel.For(0, cube.Lines, l =>
            {
                var localRes = new double[cube.Bands];
                int localCount = 0;
                for (int s = 0; s < cube.Samples; s++)
                {
                    if (mask[l, s])
                    {
                        for (int b = 0; b < cube.Bands; b++)
                        {
                            float v = cube[b, l, s];
                            if (!float.IsNaN(v)) localRes[b] += v;
                        }
                        localCount++;
                    }
                }
                lock (sync)
                {
                    for (int b = 0; b < cube.Bands; b++) res[b] += localRes[b];
                    count += localCount;
                }
            });

            var finalRes = new float[cube.Bands];
            if (count > 0)
                for (int b = 0; b < cube.Bands; b++) finalRes[b] = (float)(res[b] / count);

            _cachedVersion = cube.Version;
            _cachedSpectrum = finalRes;
            return finalRes;
        }

        protected void Lbl(Graphics g, string text, int x, int y, Color c)
        {
            using var font = new Font("Consolas", 8f, FontStyle.Bold);
            using var bg = new SolidBrush(Color.FromArgb(160, 0, 0, 0));
            using var fg = new SolidBrush(c);
            var sz = g.MeasureString(text, font);
            g.FillRectangle(bg, x, y, sz.Width, sz.Height);
            g.DrawString(text, font, fg, x, y);
        }
    }

    public class PixelShape : SelectionShape
    {
        public Point Pt { get; }
        public PixelShape(Point pt, Color c) : base(c) => Pt = pt;
        public override string ShortLabel => $"Px({Pt.X},{Pt.Y})";
        public override string LegendIcon => "📍";
        public override bool[,] GetMask(int lines, int samples)
        {
            var m = new bool[lines, samples];
            if (Pt.Y >= 0 && Pt.Y < lines && Pt.X >= 0 && Pt.X < samples) m[Pt.Y, Pt.X] = true;
            return m;
        }
        public override void DrawOn(Graphics g)
        {
            using var pen = new Pen(Color, 2f);
            g.DrawEllipse(pen, Pt.X - 3, Pt.Y - 3, 6, 6);
            Lbl(g, ShortLabel, Pt.X + 8, Pt.Y - 8, Color);
        }
        public override bool Contains(Point pt) => Math.Abs(pt.X - Pt.X) <= 3 && Math.Abs(pt.Y - Pt.Y) <= 3;
    }

    public class RectShape : SelectionShape
    {
        public Rectangle Rect { get; }
        public RectShape(Rectangle r, Color c) : base(c) => Rect = r;
        public override string ShortLabel => "Caja";
        public override string LegendIcon => "▭";
        public override bool[,] GetMask(int lines, int samples)
        {
            var m = new bool[lines, samples];
            int y0 = Math.Max(0, Rect.Top), y1 = Math.Min(lines - 1, Rect.Bottom);
            int x0 = Math.Max(0, Rect.Left), x1 = Math.Min(samples - 1, Rect.Right);
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) m[y, x] = true;
            return m;
        }
        public override void DrawOn(Graphics g)
        {
            using var fill = new SolidBrush(Color.FromArgb(28, Color));
            using var pen = new Pen(Color, 1.8f);
            g.FillRectangle(fill, Rect);
            g.DrawRectangle(pen, Rect);
            Lbl(g, ShortLabel, Rect.Left, Rect.Top - 15, Color);
        }
        public override bool Contains(Point pt) => Rect.Contains(pt);
    }

    public class CircleShape : SelectionShape
    {
        public Point Center { get; }
        public int Radius { get; }
        public CircleShape(Point c, int r, Color col) : base(col) { Center = c; Radius = r; }
        public override string ShortLabel => "Círculo";
        public override string LegendIcon => "○";
        public override bool[,] GetMask(int lines, int samples)
        {
            var m = new bool[lines, samples];
            int r2 = Radius * Radius;
            int y0 = Math.Max(0, Center.Y - Radius), y1 = Math.Min(lines - 1, Center.Y + Radius);
            int x0 = Math.Max(0, Center.X - Radius), x1 = Math.Min(samples - 1, Center.X + Radius);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if ((x - Center.X) * (x - Center.X) + (y - Center.Y) * (y - Center.Y) <= r2)
                        m[y, x] = true;
            return m;
        }
        public override void DrawOn(Graphics g)
        {
            using var fill = new SolidBrush(Color.FromArgb(28, Color));
            using var pen = new Pen(Color, 1.8f);
            g.FillEllipse(fill, Center.X - Radius, Center.Y - Radius, Radius * 2, Radius * 2);
            g.DrawEllipse(pen, Center.X - Radius, Center.Y - Radius, Radius * 2, Radius * 2);
            Lbl(g, ShortLabel, Center.X, Center.Y - Radius - 15, Color);
        }
        public override bool Contains(Point pt) => (pt.X - Center.X) * (pt.X - Center.X) + (pt.Y - Center.Y) * (pt.Y - Center.Y) <= Radius * Radius;
    }

    public class PolygonShape : SelectionShape
    {
        public Point[] Points { get; }
        public PolygonShape(IEnumerable<Point> pts, Color col) : base(col) => Points = pts.ToArray();
        public override string ShortLabel => "Polígono";
        public override string LegendIcon => "⬟";
        public override bool[,] GetMask(int lines, int samples)
        {
            var m = new bool[lines, samples];
            if (Points.Length < 3) return m;
            using var path = new GraphicsPath();
            path.AddPolygon(Points);
            var b = path.GetBounds();
            int y0 = Math.Max(0, (int)b.Top), y1 = Math.Min(lines - 1, (int)b.Bottom);
            int x0 = Math.Max(0, (int)b.Left), x1 = Math.Min(samples - 1, (int)b.Right);
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                if (path.IsVisible(x, y)) m[y, x] = true;
            return m;
        }
        public override void DrawOn(Graphics g)
        {
            if (Points.Length < 3) return;
            using var fill = new SolidBrush(Color.FromArgb(28, Color));
            using var pen = new Pen(Color, 1.8f) { LineJoin = LineJoin.Round };
            g.FillPolygon(fill, Points);
            g.DrawPolygon(pen, Points);
            int cx = (int)Points.Average(p => p.X), cy = Points.Min(p => p.Y) - 15;
            Lbl(g, ShortLabel, cx - 15, cy, Color);
        }
        public override bool Contains(Point pt)
        {
            if (Points.Length < 3) return false;
            using var path = new GraphicsPath();
            path.AddPolygon(Points);
            return path.IsVisible(pt);
        }
    }

    public class FreehandShape : SelectionShape
    {
        public Point[] Points { get; }
        public FreehandShape(IEnumerable<Point> pts, Color col) : base(col) => Points = Rdp(pts.ToList(), 2.0f).ToArray();
        public override string ShortLabel => "Lasso";
        public override string LegendIcon => "✏";
        public override bool[,] GetMask(int lines, int samples)
        {
            var m = new bool[lines, samples];
            if (Points.Length < 3) return m;
            using var path = new GraphicsPath();
            path.AddPolygon(Points);
            var b = path.GetBounds();
            int y0 = Math.Max(0, (int)b.Top), y1 = Math.Min(lines - 1, (int)b.Bottom);
            int x0 = Math.Max(0, (int)b.Left), x1 = Math.Min(samples - 1, (int)b.Right);
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                if (path.IsVisible(x, y)) m[y, x] = true;
            return m;
        }
        public override void DrawOn(Graphics g)
        {
            if (Points.Length < 3) return;
            using var fill = new SolidBrush(Color.FromArgb(28, Color));
            using var pen = new Pen(Color, 1.8f) { LineJoin = LineJoin.Round };
            g.FillPolygon(fill, Points);
            g.DrawPolygon(pen, Points);
            int cx = (int)Points.Average(p => p.X), cy = Points.Min(p => p.Y) - 15;
            Lbl(g, ShortLabel, cx - 15, cy, Color);
        }
        public override bool Contains(Point pt)
        {
            if (Points.Length < 3) return false;
            using var path = new GraphicsPath();
            path.AddPolygon(Points);
            return path.IsVisible(pt);
        }

        static List<Point> Rdp(List<Point> pts, float eps) => pts.Count <= 2 ? pts : RdpR(pts, 0, pts.Count - 1, eps);
        static List<Point> RdpR(List<Point> pts, int s, int e, float eps)
        {
            float md = 0; int mi = s;
            for (int i = s + 1; i < e; i++) { float d = Dist(pts[i], pts[s], pts[e]); if (d > md) { md = d; mi = i; } }
            if (md > eps)
            {
                var L = RdpR(pts, s, mi, eps); var R = RdpR(pts, mi, e, eps);
                L.RemoveAt(L.Count - 1); L.AddRange(R); return L;
            }
            return new List<Point> { pts[s], pts[e] };
        }
        static float Dist(Point p, Point a, Point b)
        {
            float num = Math.Abs((b.Y - a.Y) * p.X - (b.X - a.X) * p.Y + b.X * a.Y - b.Y * a.X);
            float den = (float)Math.Sqrt(Math.Pow(b.Y - a.Y, 2) + Math.Pow(b.X - a.X, 2));
            return den == 0 ? (float)Math.Sqrt(Math.Pow(p.X - a.X, 2) + Math.Pow(p.Y - a.Y, 2)) : num / den;
        }
    }

    public class MaskShape : SelectionShape
    {
        private bool[,] _mask;
        private readonly List<Point> _edgePoints = new();
        public int Width { get; }
        public int Height { get; }

        public MaskShape(bool[,] mask, Color c) : base(c)
        {
            _mask = mask;
            Height = mask.GetLength(0);
            Width = mask.GetLength(1);
            CalculateEdges();
        }

        private void CalculateEdges()
        {
            _edgePoints.Clear();
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    if (_mask[y, x])
                    {
                        bool isEdge = (x == 0 || x == Width - 1 || y == 0 || y == Height - 1) ||
                                      (!_mask[y, x - 1] || !_mask[y, x + 1] || !_mask[y - 1, x] || !_mask[y + 1, x]);
                        if (isEdge) _edgePoints.Add(new Point(x, y));
                    }
                }
            }
        }

        // --- MÉTODOS DE EDICIÓN DINÁMICA DE MÁSCARA ---
        public void AddMask(bool[,] extraMask)
        {
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    if (extraMask[y, x]) _mask[y, x] = true;
            CalculateEdges();
            InvalidateCache();
        }

        public void RemoveMask(bool[,] extraMask)
        {
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    if (extraMask[y, x]) _mask[y, x] = false;
            CalculateEdges();
            InvalidateCache();
        }

        // --- MÉTODO DE RELLENO INTELIGENTE DE HUECOS ---
        public void FillHoles()
        {
            bool[,] background = new bool[Height, Width];
            var stack = new Stack<Point>();

            for (int y = 0; y < Height; y++)
            {
                if (!_mask[y, 0]) { background[y, 0] = true; stack.Push(new Point(0, y)); }
                if (!_mask[y, Width - 1]) { background[y, Width - 1] = true; stack.Push(new Point(Width - 1, y)); }
            }
            for (int x = 0; x < Width; x++)
            {
                if (!_mask[0, x]) { background[0, x] = true; stack.Push(new Point(x, 0)); }
                if (!_mask[Height - 1, x]) { background[Height - 1, x] = true; stack.Push(new Point(x, Height - 1)); }
            }

            while (stack.Count > 0)
            {
                var p = stack.Pop();
                int cx = p.X, cy = p.Y;
                if (cx > 0 && !background[cy, cx - 1] && !_mask[cy, cx - 1]) { background[cy, cx - 1] = true; stack.Push(new Point(cx - 1, cy)); }
                if (cx < Width - 1 && !background[cy, cx + 1] && !_mask[cy, cx + 1]) { background[cy, cx + 1] = true; stack.Push(new Point(cx + 1, cy)); }
                if (cy > 0 && !background[cy - 1, cx] && !_mask[cy - 1, cx]) { background[cy - 1, cx] = true; stack.Push(new Point(cx, cy - 1)); }
                if (cy < Height - 1 && !background[cy + 1, cx] && !_mask[cy + 1, cx]) { background[cy + 1, cx] = true; stack.Push(new Point(cx, cy + 1)); }
            }

            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    if (!background[y, x]) _mask[y, x] = true;

            CalculateEdges();
            InvalidateCache();
        }

        public void Erode(int iterations = 1)
        {
            for (int i = 0; i < iterations; i++)
            {
                bool[,] newMask = new bool[Height, Width];
                for (int y = 0; y < Height; y++)
                {
                    for (int x = 0; x < Width; x++)
                    {
                        if (_mask[y, x])
                        {
                            bool keep = (x > 0 && _mask[y, x - 1]) &&
                                        (x < Width - 1 && _mask[y, x + 1]) &&
                                        (y > 0 && _mask[y - 1, x]) &&
                                        (y < Height - 1 && _mask[y + 1, x]);
                            newMask[y, x] = keep;
                        }
                    }
                }
                _mask = newMask;
            }
            CalculateEdges();
            InvalidateCache();
        }

        public void Dilate(int iterations = 1)
        {
            for (int i = 0; i < iterations; i++)
            {
                bool[,] newMask = new bool[Height, Width];
                for (int y = 0; y < Height; y++)
                {
                    for (int x = 0; x < Width; x++)
                    {
                        if (_mask[y, x])
                        {
                            newMask[y, x] = true;
                            if (x > 0) newMask[y, x - 1] = true;
                            if (x < Width - 1) newMask[y, x + 1] = true;
                            if (y > 0) newMask[y - 1, x] = true;
                            if (y < Height - 1) newMask[y + 1, x] = true;
                        }
                    }
                }
                _mask = newMask;
            }
            CalculateEdges();
            InvalidateCache();
        }

        public override string ShortLabel => "Auto-Contorno";
        public override string LegendIcon => "🪄";
        public override bool[,] GetMask(int lines, int samples) => _mask;
        public override bool Contains(Point pt) => pt.X >= 0 && pt.X < Width && pt.Y >= 0 && pt.Y < Height && _mask[pt.Y, pt.X];

        public override void DrawOn(Graphics g)
        {
            if (_edgePoints.Count == 0) return;

            using var brush = new SolidBrush(Color.FromArgb(70, Color));

            const int batchSize = 2000;
            for (int i = 0; i < _edgePoints.Count; i += batchSize)
            {
                var batch = _edgePoints.Skip(i).Take(batchSize).Select(p => new RectangleF(p.X, p.Y, 1.0f, 1.0f)).ToArray();
                g.FillRectangles(brush, batch);
            }

            if (_edgePoints.Count > 0)
            {
                int cx = (int)_edgePoints.Average(p => p.X), cy = _edgePoints.Min(p => p.Y) - 15;
                Lbl(g, ShortLabel, cx - 30, cy, Color);
            }
        }
    }
}