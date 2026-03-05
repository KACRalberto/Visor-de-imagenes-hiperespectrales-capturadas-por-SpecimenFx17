using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SpecimenFX17.Imaging
{
    /// <summary>
    /// Paletas de color para visualización BLI.
    /// Cada paleta mapea intensidad normalizada [0,1] → Color RGB.
    /// </summary>
    public enum BliColormap
    {
        /// <summary>Estándar BLI: negro→azul→cian→verde→amarillo→rojo→blanco</summary>
        Rainbow,
        /// <summary>Escala de calor: negro→rojo→naranja→amarillo→blanco</summary>
        HeatMap,
        /// <summary>Escala de frío: negro→azul→cian→blanco (emisión débil)</summary>
        ColdBlue,
        /// <summary>Escala verde fluorescente (GFP, FITC)</summary>
        GreenFluorescent,
        /// <summary>Rojo fluorescente (mCherry, Alexa647)</summary>
        RedFluorescent,
        /// <summary>Espectro visible aproximado según longitud de onda</summary>
        VisibleSpectrum,
        /// <summary>Escala de grises</summary>
        Grayscale
    }

    /// <summary>
    /// Parámetros de renderizado BLI
    /// </summary>
    public class BliRenderOptions
    {
        /// <summary>Paleta de color a utilizar</summary>
        public BliColormap Colormap    { get; set; } = BliColormap.Rainbow;
        /// <summary>Percentil inferior para escalado (0-100), default 2 %</summary>
        public float LowPercentile     { get; set; } = 2f;
        /// <summary>Percentil superior para escalado (0-100), default 98 %</summary>
        public float HighPercentile    { get; set; } = 98f;
        /// <summary>Si true, escala por percentiles; si false, usa min/max absolutos</summary>
        public bool UsePercentileScaling { get; set; } = true;
        /// <summary>Gamma de corrección (1 = lineal, < 1 aclara sombras)</summary>
        public float Gamma             { get; set; } = 1.0f;
        /// <summary>Umbral de señal: valores < umbral se muestran como fondo negro</summary>
        public float SignalThreshold   { get; set; } = 0f;
        /// <summary>Superponer la barra de escala de color</summary>
        public bool DrawColorbar       { get; set; } = true;
        /// <summary>Longitud de onda de la banda (para etiquetar)</summary>
        public double Wavelength       { get; set; } = double.NaN;
        /// <summary>Unidad de longitud de onda</summary>
        public string WavelengthUnit   { get; set; } = "nm";
    }

    /// <summary>
    /// Motor de renderizado BLI (Bioluminescence Imaging).
    ///
    /// Convierte una banda espectral en imagen pseudocolor para
    /// visualización de bioimagen de bioluminiscencia/fluorescencia.
    ///
    /// Flujo:
    ///   1. Extraer banda 2D del cubo hiperespectral
    ///   2. Normalizar con percentiles (reduce ruido de outliers)
    ///   3. Aplicar gamma y umbral de señal
    ///   4. Mapear a colores con la paleta seleccionada
    ///   5. Opcionar superposición de barra de escala
    /// </summary>
    public static class BliRenderer
    {
        // ─────────────────────────────────────────────────────────────────────
        //  Punto de entrada principal
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Renderiza la banda <paramref name="bandIndex"/> del cubo como imagen BLI.
        /// </summary>
        public static Bitmap RenderBand(HyperspectralCube cube,
                                        int bandIndex,
                                        BliRenderOptions? opts = null)
        {
            opts ??= new BliRenderOptions();

            float[,] band = cube.GetBand(bandIndex);
            int lines     = cube.Lines;
            int samples   = cube.Samples;

            // 1. Calcular rango de escalado
            (float low, float high) = opts.UsePercentileScaling
                ? ComputePercentiles(band, lines, samples,
                                     opts.LowPercentile, opts.HighPercentile)
                : cube.GetBandStats(bandIndex);

            float range = high - low;
            if (range < 1e-10f) range = 1f;

            // 2. Crear bitmap 24 bpp con datos brutos
            var bmp    = new Bitmap(samples, lines, PixelFormat.Format24bppRgb);
            var bmpData = bmp.LockBits(
                new Rectangle(0, 0, samples, lines),
                ImageLockMode.WriteOnly,
                PixelFormat.Format24bppRgb);

            int stride = bmpData.Stride;
            byte[] pixels = new byte[stride * lines];

            for (int l = 0; l < lines; l++)
            {
                int row = l * stride;
                for (int s = 0; s < samples; s++)
                {
                    float v = band[l, s];

                    // Umbral de señal → fondo negro
                    if (float.IsNaN(v) || v < opts.SignalThreshold)
                    {
                        int off = row + s * 3;
                        pixels[off] = pixels[off + 1] = pixels[off + 2] = 0;
                        continue;
                    }

                    // Normalizar [0,1]
                    float t = Math.Clamp((v - low) / range, 0f, 1f);

                    // Gamma
                    if (Math.Abs(opts.Gamma - 1f) > 1e-4f)
                        t = MathF.Pow(t, 1f / opts.Gamma);

                    // Colormap → RGB
                    var (r, g, b) = ApplyColormap(t, opts.Colormap,
                                                  opts.Wavelength,
                                                  !double.IsNaN(opts.Wavelength));

                    int o = row + s * 3;
                    pixels[o]     = b;  // BGR en Windows
                    pixels[o + 1] = g;
                    pixels[o + 2] = r;
                }
            }

            Marshal.Copy(pixels, 0, bmpData.Scan0, pixels.Length);
            bmp.UnlockBits(bmpData);

            // 3. Colorbar superpuesta
            if (opts.DrawColorbar)
                DrawColorbar(bmp, opts, low, high);

            return bmp;
        }

        /// <summary>
        /// Renderiza una imagen compuesta RGB a partir de tres bandas.
        /// </summary>
        public static Bitmap RenderRGB(HyperspectralCube cube,
                                       int redBand, int greenBand, int blueBand,
                                       BliRenderOptions? opts = null)
        {
            opts ??= new BliRenderOptions();

            float[,] r = cube.GetBand(redBand);
            float[,] g = cube.GetBand(greenBand);
            float[,] b = cube.GetBand(blueBand);

            int lines   = cube.Lines;
            int samples = cube.Samples;

            var (rl, rh) = ComputePercentiles(r, lines, samples, opts.LowPercentile, opts.HighPercentile);
            var (gl, gh) = ComputePercentiles(g, lines, samples, opts.LowPercentile, opts.HighPercentile);
            var (bl, bh) = ComputePercentiles(b, lines, samples, opts.LowPercentile, opts.HighPercentile);

            var bmp     = new Bitmap(samples, lines, PixelFormat.Format24bppRgb);
            var bmpData = bmp.LockBits(new Rectangle(0, 0, samples, lines),
                                       ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

            int stride  = bmpData.Stride;
            byte[] pixels = new byte[stride * lines];

            for (int l = 0; l < lines; l++)
            {
                int row = l * stride;
                for (int s = 0; s < samples; s++)
                {
                    byte rv = ToByte(r[l, s], rl, rh, opts.Gamma);
                    byte gv = ToByte(g[l, s], gl, gh, opts.Gamma);
                    byte bv = ToByte(b[l, s], bl, bh, opts.Gamma);

                    int o = row + s * 3;
                    pixels[o]     = bv;
                    pixels[o + 1] = gv;
                    pixels[o + 2] = rv;
                }
            }

            Marshal.Copy(pixels, 0, bmpData.Scan0, pixels.Length);
            bmp.UnlockBits(bmpData);

            return bmp;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Colormaps
        // ─────────────────────────────────────────────────────────────────────

        private static (byte R, byte G, byte B) ApplyColormap(
            float t, BliColormap map, double wavelength, bool useWavelength)
        {
            return map switch
            {
                BliColormap.Rainbow          => Rainbow(t),
                BliColormap.HeatMap          => HeatMap(t),
                BliColormap.ColdBlue         => ColdBlue(t),
                BliColormap.GreenFluorescent => GreenFluorescent(t),
                BliColormap.RedFluorescent   => RedFluorescent(t),
                BliColormap.VisibleSpectrum  => useWavelength
                                               ? WavelengthToRgb(wavelength, t)
                                               : Rainbow(t),
                BliColormap.Grayscale        => (ToByte(t), ToByte(t), ToByte(t)),
                _ => Rainbow(t)
            };
        }

        /// <summary>Arcoíris: negro→azul→cian→verde→amarillo→rojo→blanco</summary>
        private static (byte, byte, byte) Rainbow(float t)
        {
            // Mapeamos [0,1] a través de 6 segmentos de color
            float r, g, b;

            if (t < 0.125f) { r = 0;       g = 0;       b = 0.5f + t * 4f; }
            else if (t < 0.375f) { r = 0;   g = (t - 0.125f) * 4f; b = 1f; }
            else if (t < 0.625f) { r = (t - 0.375f) * 4f; g = 1f; b = 1f - (t - 0.375f) * 4f; }
            else if (t < 0.875f) { r = 1f;  g = 1f - (t - 0.625f) * 4f; b = 0f; }
            else                 { r = 1f;  g = (t - 0.875f) * 8f; b = (t - 0.875f) * 8f; }

            return (ToByte(r), ToByte(g), ToByte(b));
        }

        /// <summary>Mapa de calor: negro→rojo→naranja→amarillo→blanco</summary>
        private static (byte, byte, byte) HeatMap(float t)
        {
            float r = Math.Clamp(t * 3f, 0, 1);
            float g = Math.Clamp(t * 3f - 1f, 0, 1);
            float b = Math.Clamp(t * 3f - 2f, 0, 1);
            return (ToByte(r), ToByte(g), ToByte(b));
        }

        /// <summary>Azul frío: negro→azul→cian→blanco</summary>
        private static (byte, byte, byte) ColdBlue(float t)
        {
            float r = Math.Clamp(t * 2f - 1f, 0, 1);
            float g = Math.Clamp(t * 2f - 1f, 0, 1);
            float b = Math.Clamp(t * 2f, 0, 1);
            return (ToByte(r), ToByte(g), ToByte(b));
        }

        /// <summary>Verde fluorescente (GFP, FITC): negro→verde oscuro→verde brillante→blanco-verdoso</summary>
        private static (byte, byte, byte) GreenFluorescent(float t)
        {
            float r = Math.Clamp(t * 2f - 1f, 0, 1) * 0.5f;
            float g = Math.Clamp(t * 1.5f, 0, 1);
            float b = Math.Clamp(t * 0.5f, 0, 1);
            return (ToByte(r), ToByte(g), ToByte(b));
        }

        /// <summary>Rojo fluorescente (mCherry, Cy3, Alexa 594): negro→rojo oscuro→rojo vivo→blanco-rojizo</summary>
        private static (byte, byte, byte) RedFluorescent(float t)
        {
            float r = Math.Clamp(t * 1.5f, 0, 1);
            float g = Math.Clamp(t * 0.5f, 0, 1) * 0.3f;
            float b = 0;
            return (ToByte(r), ToByte(g), ToByte(b));
        }

        /// <summary>
        /// Convierte longitud de onda física (nm) a color RGB visible,
        /// modulando la intensidad con t.
        /// Válido para ~380–780 nm.
        /// </summary>
        public static (byte R, byte G, byte B) WavelengthToRgb(double nm, float intensity = 1f)
        {
            double r = 0, g = 0, b = 0;

            if      (nm >= 380 && nm < 440) { r = -(nm - 440) / 60.0; g = 0;                   b = 1; }
            else if (nm >= 440 && nm < 490) { r = 0;                   g = (nm - 440) / 50.0;   b = 1; }
            else if (nm >= 490 && nm < 510) { r = 0;                   g = 1;                   b = -(nm - 510) / 20.0; }
            else if (nm >= 510 && nm < 580) { r = (nm - 510) / 70.0;  g = 1;                   b = 0; }
            else if (nm >= 580 && nm < 645) { r = 1;                   g = -(nm - 645) / 65.0;  b = 0; }
            else if (nm >= 645 && nm <= 780){ r = 1;                   g = 0;                   b = 0; }
            // UV/IR → paleta de sustitución
            else if (nm < 380) { r = 0.5; g = 0; b = 1; }
            else               { r = 1;   g = 0; b = 0; }

            // Atenuación en los extremos del espectro visible
            double factor = 1.0;
            if      (nm >= 380 && nm < 420) factor = 0.3 + 0.7 * (nm - 380) / 40;
            else if (nm >= 700 && nm <= 780) factor = 0.3 + 0.7 * (780 - nm) / 80;

            return (ToByte((float)(r * factor * intensity)),
                    ToByte((float)(g * factor * intensity)),
                    ToByte((float)(b * factor * intensity)));
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Barra de escala de color
        // ─────────────────────────────────────────────────────────────────────

        private static void DrawColorbar(Bitmap bmp, BliRenderOptions opts,
                                         float low, float high)
        {
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            int barW = 16, barH = Math.Min(bmp.Height - 40, 120);
            int x0   = bmp.Width - barW - 10;
            int y0   = 20;

            // Gradiente vertical de la barra
            for (int i = 0; i < barH; i++)
            {
                float t = 1f - (float)i / barH;
                var (r, gc, b) = ApplyColormap(t, opts.Colormap, opts.Wavelength,
                                               !double.IsNaN(opts.Wavelength));
                using var pen = new Pen(Color.FromArgb(r, gc, b));
                g.DrawLine(pen, x0, y0 + i, x0 + barW, y0 + i);
            }

            // Borde
            using var border = new Pen(Color.White, 1);
            g.DrawRectangle(border, x0, y0, barW, barH);

            // Etiquetas
            using var font   = new Font("Arial", 7, FontStyle.Regular);
            using var brush  = new SolidBrush(Color.White);
            string loStr = FormatValue(low);
            string hiStr = FormatValue(high);

            g.DrawString(hiStr, font, brush, x0 - 2, y0 - 1);
            g.DrawString(loStr, font, brush, x0 - 2, y0 + barH + 1);

            // Título de longitud de onda
            if (!double.IsNaN(opts.Wavelength))
            {
                string label = $"{opts.Wavelength:F1} {opts.WavelengthUnit}";
                using var titleFont = new Font("Arial", 7, FontStyle.Bold);
                var sz = g.MeasureString(label, titleFont);
                g.DrawString(label, titleFont, brush,
                             bmp.Width - sz.Width - 5, y0 + barH + 10);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Estadísticas
        // ─────────────────────────────────────────────────────────────────────

        private static (float Low, float High) ComputePercentiles(
            float[,] data, int lines, int samples, float lowPct, float highPct)
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

            var sorted = vals[..idx];
            Array.Sort(sorted);
            if (sorted.Length == 0) return (0f, 1f);

            int lo = (int)(sorted.Length * lowPct  / 100f);
            int hi = (int)(sorted.Length * highPct / 100f) - 1;
            hi = Math.Clamp(hi, 0, sorted.Length - 1);
            lo = Math.Clamp(lo, 0, hi);

            return (sorted[lo], sorted[hi]);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Utilidades internas
        // ─────────────────────────────────────────────────────────────────────

        private static byte ToByte(float v)              => (byte)(Math.Clamp(v, 0f, 1f) * 255f);
        private static byte ToByte(float v, float lo, float hi, float gamma)
        {
            float rng = hi - lo; if (rng < 1e-10f) rng = 1f;
            float t = Math.Clamp((v - lo) / rng, 0f, 1f);
            if (Math.Abs(gamma - 1f) > 1e-4f) t = MathF.Pow(t, 1f / gamma);
            return ToByte(t);
        }

        private static string FormatValue(float v) =>
            Math.Abs(v) >= 1000 ? v.ToString("0.0e0") :
            Math.Abs(v) >= 0.01 ? v.ToString("F2")   : v.ToString("0.##e0");
    }
}
