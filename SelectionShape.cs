// SelectionShape.cs  — Jerarquía de formas de selección para SpecimenFX17
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;

namespace SpecimenFX17.Imaging
{
    public enum SelectionTool { Rectangle, Polygon, Circle, Freehand }

    // ── Clase base ────────────────────────────────────────────────────────────
    public abstract class SelectionShape
    {
        public Color Color { get; set; }
        public abstract string ShortLabel { get; }
        public abstract string LegendIcon { get; }

        // ── Metadatos para etiquetado (Ground Truth) ──
        public string Variety { get; set; } = "";
        public string Date { get; set; } = DateTime.Now.ToString("yyyy-MM-dd");
        public float? MeasuredBrix { get; set; } = null;
        public string Notes { get; set; } = "";

        protected SelectionShape(Color c) => Color = c;

        public abstract bool[,] GetMask(int lines, int samples);
        public abstract void DrawOn(Graphics g);
        public abstract bool Contains(Point pt); // Nuevo: Para detectar el clic derecho

        public virtual float[] GetSpectrum(HyperspectralCube cube)
        {
            var mask = GetMask(cube.Lines, cube.Samples);
            var res = new float[cube.Bands];
            int cnt = 0;
            for (int y = 0; y < cube.Lines; y++)
                for (int x = 0; x < cube.Samples; x++)
                {
                    if (!mask[y, x]) continue;
                    for (int b = 0; b < cube.Bands; b++) res[b] += cube[b, y, x];
                    cnt++;
                }
            if (cnt > 0)
                for (int b = 0; b < cube.Bands; b++) res[b] /= cnt;
            return res;
        }

        protected static bool[,] BitmapMask(int lines, int samples, Action<Graphics> draw)
        {
            using var bmp = new Bitmap(samples, lines, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            { g.Clear(Color.Black); g.SmoothingMode = SmoothingMode.None; draw(g); }
            var bd = bmp.LockBits(new Rectangle(0, 0, samples, lines),
                                    ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var buf = new byte[bd.Stride * lines];
            Marshal.Copy(bd.Scan0, buf, 0, buf.Length);
            bmp.UnlockBits(bd);
            var mask = new bool[lines, samples];
            int st = bd.Stride;
            for (int y = 0; y < lines; y++)
                for (int x = 0; x < samples; x++)
                    mask[y, x] = buf[y * st + x * 4] > 128;
            return mask;
        }

        protected static void Lbl(Graphics g, string t, float x, float y, Color col)
        {
            using var f = new Font("Consolas", 7f, FontStyle.Bold);
            using var bg = new SolidBrush(Color.FromArgb(160, 0, 0, 0));
            using var br = new SolidBrush(col);
            var sz = g.MeasureString(t, f);
            if (y < 0) y = 2;
            g.FillRectangle(bg, x - 1, y - 1, sz.Width + 2, sz.Height + 2);
            g.DrawString(t, f, br, x, y);
        }

        protected static void Corners(Graphics g, Pen p, Rectangle r, int s = 8)
        {
            g.DrawLine(p, r.Left, r.Top, r.Left + s, r.Top);
            g.DrawLine(p, r.Left, r.Top, r.Left, r.Top + s);
            g.DrawLine(p, r.Right, r.Top, r.Right - s, r.Top);
            g.DrawLine(p, r.Right, r.Top, r.Right, r.Top + s);
            g.DrawLine(p, r.Left, r.Bottom, r.Left + s, r.Bottom);
            g.DrawLine(p, r.Left, r.Bottom, r.Left, r.Bottom - s);
            g.DrawLine(p, r.Right, r.Bottom, r.Right - s, r.Bottom);
            g.DrawLine(p, r.Right, r.Bottom, r.Right, r.Bottom - s);
        }
    }

    // ── Píxel ─────────────────────────────────────────────────────────────────
    public sealed class PixelShape : SelectionShape
    {
        public Point Pt { get; }
        public PixelShape(Point pt, Color c) : base(c) => Pt = pt;
        public override string ShortLabel => $"{(MeasuredBrix.HasValue ? $"{MeasuredBrix.Value:F1}°Bx " : "")}({Pt.X},{Pt.Y})";
        public override string LegendIcon => "·";
        public override bool Contains(Point pt) => Math.Abs(Pt.X - pt.X) <= 5 && Math.Abs(Pt.Y - pt.Y) <= 5;
        public override bool[,] GetMask(int L, int S)
        {
            var m = new bool[L, S];
            if (Pt.X >= 0 && Pt.X < S && Pt.Y >= 0 && Pt.Y < L) m[Pt.Y, Pt.X] = true;
            return m;
        }
        public override float[] GetSpectrum(HyperspectralCube cube) => cube.GetSpectrum(Pt.Y, Pt.X);
        public override void DrawOn(Graphics g)
        {
            int r = 7;
            using var bg = new Pen(Color.FromArgb(180, 0, 0, 0), 3.5f);
            g.DrawLine(bg, Pt.X - r, Pt.Y, Pt.X + r, Pt.Y);
            g.DrawLine(bg, Pt.X, Pt.Y - r, Pt.X, Pt.Y + r);
            g.DrawEllipse(bg, Pt.X - r / 2, Pt.Y - r / 2, r, r);
            using var pen = new Pen(Color, 1.8f);
            g.DrawLine(pen, Pt.X - r, Pt.Y, Pt.X + r, Pt.Y);
            g.DrawLine(pen, Pt.X, Pt.Y - r, Pt.X, Pt.Y + r);
            g.DrawEllipse(pen, Pt.X - r / 2, Pt.Y - r / 2, r, r);
            Lbl(g, ShortLabel, Pt.X + r + 2, Pt.Y - 8, Color);
        }
    }

    // ── Rectángulo ────────────────────────────────────────────────────────────
    public sealed class RectShape : SelectionShape
    {
        public Rectangle Rect { get; }
        public RectShape(Rectangle rect, Color c) : base(c) => Rect = rect;
        public override string ShortLabel => $"{(MeasuredBrix.HasValue ? $"{MeasuredBrix.Value:F1}°Bx " : "")}Rect {Rect.Width}x{Rect.Height}";
        public override string LegendIcon => "▭";
        public override bool Contains(Point pt) => Rect.Contains(pt);
        public override bool[,] GetMask(int L, int S)
        {
            var m = new bool[L, S];
            int r1 = Math.Max(0, Rect.Top), r2 = Math.Min(L, Rect.Bottom + 1);
            int c1 = Math.Max(0, Rect.Left), c2 = Math.Min(S, Rect.Right + 1);
            for (int y = r1; y < r2; y++)
                for (int x = c1; x < c2; x++) m[y, x] = true;
            return m;
        }
        public override void DrawOn(Graphics g)
        {
            using var fill = new SolidBrush(Color.FromArgb(30, Color.R, Color.G, Color.B));
            g.FillRectangle(fill, Rect);
            using var pen = new Pen(Color, 1.8f); g.DrawRectangle(pen, Rect);
            using var cp = new Pen(Color, 2.5f); Corners(g, cp, Rect);
            float ly = Rect.Top - 12; if (ly < 0) ly = Rect.Top + 2;
            Lbl(g, ShortLabel, Rect.Left + 2, ly, Color);
        }
    }

    // ── Polígono ──────────────────────────────────────────────────────────────
    public sealed class PolygonShape : SelectionShape
    {
        public Point[] Vertices { get; }
        public PolygonShape(IEnumerable<Point> v, Color c) : base(c) => Vertices = v.ToArray();
        public override string ShortLabel => $"{(MeasuredBrix.HasValue ? $"{MeasuredBrix.Value:F1}°Bx " : "")}Pol {Vertices.Length}v";
        public override string LegendIcon => "⬟";
        public override bool Contains(Point pt)
        {
            using var gp = new GraphicsPath();
            gp.AddPolygon(Vertices.Select(v => new PointF(v.X, v.Y)).ToArray());
            return gp.IsVisible(pt);
        }
        public override bool[,] GetMask(int L, int S)
        {
            if (Vertices.Length < 3) return new bool[L, S];
            var pts = Vertices.Select(p => new PointF(p.X, p.Y)).ToArray();
            return BitmapMask(L, S, g => g.FillPolygon(Brushes.White, pts));
        }
        public override void DrawOn(Graphics g)
        {
            if (Vertices.Length < 2) return;
            var pts = Vertices.Select(p => new PointF(p.X, p.Y)).ToArray();
            using var fill = new SolidBrush(Color.FromArgb(28, Color.R, Color.G, Color.B));
            if (pts.Length >= 3) g.FillPolygon(fill, pts);
            using var pen = new Pen(Color, 1.8f) { LineJoin = LineJoin.Round };
            g.DrawPolygon(pen, pts);
            foreach (var v in Vertices)
            {
                using var vb = new SolidBrush(Color.FromArgb(190, 0, 0, 0));
                g.FillEllipse(vb, v.X - 4, v.Y - 4, 8, 8);
                using var vp = new Pen(Color, 1.5f); g.DrawEllipse(vp, v.X - 4, v.Y - 4, 8, 8);
            }
            int cx2 = Vertices.Sum(p => p.X) / Vertices.Length, cy2 = Vertices.Min(p => p.Y) - 12;
            Lbl(g, ShortLabel, cx2 - 40, cy2, Color);
        }
    }

    // ── Círculo ───────────────────────────────────────────────────────────────
    public sealed class CircleShape : SelectionShape
    {
        public Point Center { get; }
        public int Radius { get; }
        public CircleShape(Point center, int radius, Color c) : base(c)
        { Center = center; Radius = Math.Max(1, radius); }
        public override string ShortLabel => $"{(MeasuredBrix.HasValue ? $"{MeasuredBrix.Value:F1}°Bx " : "")}Circ r={Radius}";
        public override string LegendIcon => "○";
        public override bool Contains(Point pt) => Math.Pow(Center.X - pt.X, 2) + Math.Pow(Center.Y - pt.Y, 2) <= Radius * Radius;
        public override bool[,] GetMask(int L, int S)
            => BitmapMask(L, S, g => g.FillEllipse(Brushes.White,
                Center.X - Radius, Center.Y - Radius, Radius * 2, Radius * 2));
        public override void DrawOn(Graphics g)
        {
            int x = Center.X - Radius, y = Center.Y - Radius, d = Radius * 2;
            using var fill = new SolidBrush(Color.FromArgb(28, Color.R, Color.G, Color.B));
            g.FillEllipse(fill, x, y, d, d);
            using var pen = new Pen(Color, 1.8f); g.DrawEllipse(pen, x, y, d, d);
            using var cp = new Pen(Color, 1.5f);
            g.DrawLine(cp, Center.X - 6, Center.Y, Center.X + 6, Center.Y);
            g.DrawLine(cp, Center.X, Center.Y - 6, Center.X, Center.Y + 6);
            Lbl(g, ShortLabel, Center.X - Radius + 2, Center.Y - Radius - 13, Color);
        }
    }

    // ── Trazo libre ───────────────────────────────────────────────────────────
    public sealed class FreehandShape : SelectionShape
    {
        public Point[] Points { get; }
        public FreehandShape(IEnumerable<Point> pts, Color c) : base(c)
            => Points = Rdp(pts.ToList(), 2f).ToArray();
        public override string ShortLabel => $"{(MeasuredBrix.HasValue ? $"{MeasuredBrix.Value:F1}°Bx " : "")}Lasso";
        public override string LegendIcon => "✏";
        public override bool Contains(Point pt)
        {
            if (Points.Length < 3) return false;
            using var gp = new GraphicsPath();
            gp.AddPolygon(Points.Select(v => new PointF(v.X, v.Y)).ToArray());
            return gp.IsVisible(pt);
        }
        public override bool[,] GetMask(int L, int S)
        {
            if (Points.Length < 3) return new bool[L, S];
            var pts = Points.Select(p => new PointF(p.X, p.Y)).ToArray();
            return BitmapMask(L, S, g => g.FillPolygon(Brushes.White, pts));
        }
        public override void DrawOn(Graphics g)
        {
            if (Points.Length < 2) return;
            var pts = Points.Select(p => new PointF(p.X, p.Y)).ToArray();
            using var fill = new SolidBrush(Color.FromArgb(28, Color.R, Color.G, Color.B));
            if (pts.Length >= 3) g.FillPolygon(fill, pts);
            using var pen = new Pen(Color, 1.8f) { LineJoin = LineJoin.Round };
            g.DrawPolygon(pen, pts);
            int cx = Points.Sum(p => p.X) / Points.Length, cy = Points.Min(p => p.Y) - 12;
            Lbl(g, ShortLabel, cx - 30, cy, Color);
        }

        static List<Point> Rdp(List<Point> pts, float eps)
            => pts.Count <= 2 ? pts : RdpR(pts, 0, pts.Count - 1, eps);
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
            if (a == b) return (float)Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));
            float num = Math.Abs((b.X - a.X) * (a.Y - p.Y) - (a.X - p.X) * (b.Y - a.Y));
            float den = (float)Math.Sqrt((b.X - a.X) * (double)(b.X - a.X) + (b.Y - a.Y) * (double)(b.Y - a.Y));
            return den < 1e-6f ? 0f : num / den;
        }
    }
}