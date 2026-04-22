using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;

namespace SpecimenFX17.Imaging
{
    public static class AutoSegmenter
    {
        /// <summary>
        /// FUNCIÓN COMPARTIDA: Genera la máscara binaria base.
        /// Al usar esta función en la Interfaz y en el Batch, garantizamos que lo que ves es lo que se guarda.
        /// </summary>
        public static Mat GetRawMask(Mat gray8U, SegmentationParams p)
        {
            Mat binary = new Mat();

            // Si el fondo es blanco y el objeto oscuro, necesitamos invertir el umbral
            if (p.InvertThreshold)
                Cv2.Threshold(gray8U, binary, p.Threshold, 255, ThresholdTypes.BinaryInv);
            else
                Cv2.Threshold(gray8U, binary, p.Threshold, 255, ThresholdTypes.Binary);

            Mat kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(5, 5));

            if (p.OpenIters > 0)
                Cv2.MorphologyEx(binary, binary, MorphTypes.Open, kernel, iterations: p.OpenIters);

            if (p.CloseIters > 0)
                Cv2.MorphologyEx(binary, binary, MorphTypes.Close, kernel, iterations: p.CloseIters);

            return binary;
        }

        public static async Task<List<SelectionShape>> SegmentCubeAsync(HyperspectralCube cube, int targetBand = 65, SegmentationParams? p = null, IProgress<int>? progress = null, CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                if (p == null) p = new SegmentationParams();

                int w = cube.Samples;
                int h = cube.Lines;

                progress?.Report(10);
                ct.ThrowIfCancellationRequested();

                // 1. Extraer banda y normalizar a 8 bits
                using Mat gray8U = new Mat(h, w, MatType.CV_8UC1);
                float min = float.MaxValue, max = float.MinValue;

                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        float v = cube[targetBand, y, x];
                        if (!float.IsNaN(v) && !float.IsInfinity(v))
                        {
                            if (v < min) min = v;
                            if (v > max) max = v;
                        }
                    }
                }

                float range = max - min;
                if (range <= 0) range = 1f;

                var indexer = gray8U.GetGenericIndexer<byte>();
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        float v = cube[targetBand, y, x];
                        if (float.IsNaN(v) || float.IsInfinity(v))
                            indexer[y, x] = 0;
                        else
                            indexer[y, x] = (byte)(Math.Clamp((v - min) / range, 0f, 1f) * 255);
                    }
                }

                progress?.Report(40);
                ct.ThrowIfCancellationRequested();

                // 2. OBTENER LA MÁSCARA EXACTA QUE VE EL USUARIO EN LA PREVIEW
                using Mat binary = GetRawMask(gray8U, p);

                progress?.Report(70);

                // 3. Separar en objetos (Connected Components)
                using Mat labels = new Mat();
                using Mat stats = new Mat();
                using Mat centroids = new Mat();

                int numLabels = Cv2.ConnectedComponentsWithStats(binary, labels, stats, centroids);

                List<SelectionShape> rois = new List<SelectionShape>();
                Random rnd = new Random();
                int objectCount = 1;

                var labelIndexer = labels.GetGenericIndexer<int>();

                // Iterar sobre los objetos encontrados (Empezamos en 1, el 0 es el fondo)
                for (int i = 1; i < numLabels; i++)
                {
                    int area = stats.At<int>(i, (int)ConnectedComponentsTypes.Area);

                    // Solo guardamos si supera el área mínima
                    if (area >= p.MinArea)
                    {
                        bool[,] objMask = new bool[h, w];
                        for (int y = 0; y < h; y++)
                        {
                            for (int x = 0; x < w; x++)
                            {
                                if (labelIndexer[y, x] == i)
                                    objMask[y, x] = true;
                            }
                        }

                        System.Drawing.Color c = System.Drawing.Color.FromArgb(rnd.Next(50, 255), rnd.Next(50, 255), rnd.Next(50, 255));
                        var shape = Activator.CreateInstance(Type.GetType("SpecimenFX17.Imaging.MaskShape")!, new object[] { objMask, c }) as SelectionShape;

                        if (shape != null)
                        {
                            shape.Variety = $"Instancia_{objectCount++:D3}";
                            shape.Notes = "AutoSegmentado";
                            rois.Add(shape);
                        }
                    }
                }

                progress?.Report(100);
                return rois;
            }, ct);
        }
    }
}