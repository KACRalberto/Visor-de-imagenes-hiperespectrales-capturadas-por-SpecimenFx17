using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace SpecimenFX17.Imaging
{
    public enum BliColormap { Rainbow, HeatMap, ColdBlue, GreenFluorescent, RedFluorescent, VisibleSpectrum, Grayscale }

    public class BliRenderOptions
    {
        public BliColormap Colormap { get; set; } = BliColormap.Rainbow;
        public float LowPercentile { get; set; } = 2f;
        public float HighPercentile { get; set; } = 98f;
        public float Gamma { get; set; } = 1.0f;
        public float SignalThreshold { get; set; } = 0f;
        public bool DrawColorbar { get; set; } = true;
        public double Wavelength { get; set; } = 0;
        public string WavelengthUnit { get; set; } = "nm";
    }

    public static class BliRenderer
    {
        public static Bitmap RenderBand(HyperspectralCube cube, int bandIndex, BliRenderOptions opts)
        {
            int lines = cube.Lines, samples = cube.Samples;
            var data = cube.GetBand(bandIndex);
            var (lo, hi) = GetPercentiles(data, lines, samples, opts.LowPercentile, opts.HighPercentile);

            var bmp = new Bitmap(samples, lines, PixelFormat.Format24bppRgb);
            var bd = bmp.LockBits(new Rectangle(0, 0, samples, lines), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            byte[] pixels = new byte[bd.Stride * lines];

            float range = hi - lo <= 0 ? 1e-6f : hi - lo;
            bool applyGamma = Math.Abs(opts.Gamma - 1f) > 0.001f;

            Parallel.For(0, lines, l =>
            {
                int rowOff = l * bd.Stride;
                for (int s = 0; s < samples; s++)
                {
                    float v = data[l, s];
                    int pOff = rowOff + s * 3;

                    if (float.IsNaN(v) || v < opts.SignalThreshold)
                    {
                        pixels[pOff] = 0; pixels[pOff + 1] = 0; pixels[pOff + 2] = 0;
                        continue;
                    }

                    float t = Math.Clamp((v - lo) / range, 0f, 1f);
                    if (applyGamma) t = (float)Math.Pow(t, opts.Gamma);

                    var (r, g, b) = GetColor(t, opts.Colormap);
                    pixels[pOff] = b; pixels[pOff + 1] = g; pixels[pOff + 2] = r;
                }
            });

            Marshal.Copy(pixels, 0, bd.Scan0, pixels.Length);
            bmp.UnlockBits(bd);

            if (opts.DrawColorbar) DrawColorbarOnBitmap(bmp, lo, hi, opts.Colormap);
            return bmp;
        }

        public static Bitmap RenderRGB(HyperspectralCube cube, int bandR, int bandG, int bandB, BliRenderOptions opts)
        {
            int lines = cube.Lines, samples = cube.Samples;

            var dataR = cube.GetBand(bandR); var dataG = cube.GetBand(bandG); var dataB = cube.GetBand(bandB);

            var (loR, hiR) = GetPercentiles(dataR, lines, samples, opts.LowPercentile, opts.HighPercentile);
            var (loG, hiG) = GetPercentiles(dataG, lines, samples, opts.LowPercentile, opts.HighPercentile);
            var (loB, hiB) = GetPercentiles(dataB, lines, samples, opts.LowPercentile, opts.HighPercentile);

            var bmp = new Bitmap(samples, lines, PixelFormat.Format24bppRgb);
            var bd = bmp.LockBits(new Rectangle(0, 0, samples, lines), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            byte[] pixels = new byte[bd.Stride * lines];

            float rangeR = hiR - loR <= 0 ? 1e-6f : hiR - loR;
            float rangeG = hiG - loG <= 0 ? 1e-6f : hiG - loG;
            float rangeB = hiB - loB <= 0 ? 1e-6f : hiB - loB;
            bool applyGamma = Math.Abs(opts.Gamma - 1f) > 0.001f;

            Parallel.For(0, lines, l =>
            {
                int rowOff = l * bd.Stride;
                for (int s = 0; s < samples; s++)
                {
                    float vr = dataR[l, s], vg = dataG[l, s], vb = dataB[l, s];
                    int pOff = rowOff + s * 3;

                    if (float.IsNaN(vr) || float.IsNaN(vg) || float.IsNaN(vb))
                    {
                        pixels[pOff] = 0; pixels[pOff + 1] = 0; pixels[pOff + 2] = 0;
                        continue;
                    }

                    float tr = Math.Clamp((vr - loR) / rangeR, 0f, 1f);
                    float tg = Math.Clamp((vg - loG) / rangeG, 0f, 1f);
                    float tb = Math.Clamp((vb - loB) / rangeB, 0f, 1f);

                    if (applyGamma)
                    {
                        tr = (float)Math.Pow(tr, opts.Gamma); tg = (float)Math.Pow(tg, opts.Gamma); tb = (float)Math.Pow(tb, opts.Gamma);
                    }

                    pixels[pOff] = ToByte(tb); pixels[pOff + 1] = ToByte(tg); pixels[pOff + 2] = ToByte(tr);
                }
            });

            Marshal.Copy(pixels, 0, bd.Scan0, pixels.Length);
            bmp.UnlockBits(bd);
            return bmp;
        }

        private static void DrawColorbarOnBitmap(Bitmap bmp, float min, float max, BliColormap colormap)
        {
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int barH = Math.Min(120, bmp.Height - 30), barW = 14;
            int bx = bmp.Width - barW - 8, by = 15;

            for (int i = 0; i < barH; i++)
            {
                float t = 1f - (float)i / barH;
                var (r, gc, b) = GetColor(t, colormap);
                using var pen = new Pen(Color.FromArgb(r, gc, b));
                g.DrawLine(pen, bx, by + i, bx + barW, by + i);
            }
            g.DrawRectangle(Pens.White, bx, by, barW, barH);
            using var font = new Font("Arial", 7f);
            g.DrawString(max.ToString("G4"), font, Brushes.White, bx - 2, by - 1);
            g.DrawString(min.ToString("G4"), font, Brushes.White, bx - 2, by + barH + 1);
        }

        private static (float Lo, float Hi) GetPercentiles(float[,] data, int lines, int samples, float lowPct, float highPct)
        {
            int n = lines * samples;
            var vals = new float[n];
            int idx = 0;
            for (int l = 0; l < lines; l++)
                for (int s = 0; s < samples; s++)
                {
                    float v = data[l, s];
                    if (!float.IsNaN(v)) vals[idx++] = v;
                }

            if (idx == 0) return (0f, 1f);
            Array.Sort(vals, 0, idx);

            int lo = (int)(idx * lowPct / 100f);
            int hi = (int)(idx * highPct / 100f) - 1;
            hi = Math.Clamp(hi, 0, idx - 1);
            lo = Math.Clamp(lo, 0, hi);

            return (vals[lo], vals[hi]);
        }

        private static (byte R, byte G, byte B) GetColor(float t, BliColormap map)
        {
            float r, g, b;
            switch (map)
            {
                case BliColormap.HeatMap: return (ToByte(Math.Clamp(t * 3f, 0, 1)), ToByte(Math.Clamp(t * 3f - 1f, 0, 1)), ToByte(Math.Clamp(t * 3f - 2f, 0, 1)));
                case BliColormap.Grayscale: return (ToByte(t), ToByte(t), ToByte(t));
                case BliColormap.ColdBlue: return (ToByte(Math.Clamp(t * 2 - 1, 0, 1)), ToByte(Math.Clamp(t * 2 - 1, 0, 1)), ToByte(Math.Clamp(t * 2, 0, 1)));
                case BliColormap.GreenFluorescent: return (ToByte(Math.Clamp(t * 2 - 1, 0, 1) * 0.5f), ToByte(Math.Clamp(t * 1.5f, 0, 1)), ToByte(Math.Clamp(t * 0.5f, 0, 1)));
                case BliColormap.RedFluorescent: return (ToByte(Math.Clamp(t * 1.5f, 0, 1)), ToByte(Math.Clamp(t * 0.5f, 0, 1) * 0.3f), 0);
                default:
                    if (t < 0.125f) { r = 0; g = 0; b = 0.5f + t * 4f; }
                    else if (t < 0.375f) { r = 0; g = (t - .125f) * 4f; b = 1f; }
                    else if (t < 0.625f) { r = (t - .375f) * 4f; g = 1f; b = 1f - (t - .375f) * 4f; }
                    else if (t < 0.875f) { r = 1f; g = 1f - (t - .625f) * 4f; b = 0f; }
                    else { r = 1f; g = (t - .875f) * 8f; b = (t - .875f) * 8f; }
                    return (ToByte(r), ToByte(g), ToByte(b));
            }
        }
        private static byte ToByte(float v) => (byte)(Math.Clamp(v, 0f, 1f) * 255f);
    }
}