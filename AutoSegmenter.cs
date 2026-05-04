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
        // Genera un color aleatorio con buena saturación/brillo
        private static System.Drawing.Color GetRandomColor()
        {
            var rnd = new Random();
            return System.Drawing.Color.FromArgb(rnd.Next(100, 256), rnd.Next(100, 256), rnd.Next(100, 256));
        }

        // FUNCIÓN CENTRAL COMPARTIDA ENTRE LA UI Y EL BATCH
        public static Mat GetRawMask(Mat gray8U, SegmentationParams p)
        {
            Mat binary = new Mat();

            // Aseguramos que BlockSize sea siempre impar y mínimo 3
            int blockSize = Math.Max(3, p.BlockSize | 1);

            // UMBRAL ADAPTATIVO: Inmune a cambios de brillo global
            Cv2.AdaptiveThreshold(
                gray8U,
                binary,
                255,
                AdaptiveThresholdTypes.GaussianC,
                p.InvertThreshold ? ThresholdTypes.BinaryInv : ThresholdTypes.Binary,
                blockSize,
                p.ConstantC
            );

            // Morfología (Limpieza)
            Mat kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(5, 5));
            if (p.OpenIters > 0) Cv2.MorphologyEx(binary, binary, MorphTypes.Open, kernel, iterations: p.OpenIters);
            if (p.CloseIters > 0) Cv2.MorphologyEx(binary, binary, MorphTypes.Close, kernel, iterations: p.CloseIters);

            // BORRADO MANUAL (FloodFill)
            if (p.PointsToRemove != null && p.PointsToRemove.Count > 0)
            {
                foreach (var pt in p.PointsToRemove)
                {
                    if (pt.X >= 0 && pt.X < binary.Cols && pt.Y >= 0 && pt.Y < binary.Rows)
                    {
                        if (binary.At<byte>(pt.Y, pt.X) > 0)
                        {
                            Cv2.FloodFill(binary, new OpenCvSharp.Point(pt.X, pt.Y), new Scalar(0));
                        }
                    }
                }
            }

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

                float range = max - min <= 0 ? 1f : max - min;
                var indexer = gray8U.GetGenericIndexer<byte>();

                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        float v = cube[targetBand, y, x];
                        indexer[y, x] = float.IsNaN(v) || float.IsInfinity(v) ? (byte)0 : (byte)(Math.Clamp((v - min) / range, 0f, 1f) * 255);
                    }
                }

                progress?.Report(40);
                ct.ThrowIfCancellationRequested();

                // 2. Extraer la máscara limpia
                using Mat binary = GetRawMask(gray8U, p);

                progress?.Report(70);

                // 3. Etiquetado de Componentes Conectados (Detectar múltiples objetos)
                using Mat labels = new Mat();
                using Mat stats = new Mat();
                using Mat centroids = new Mat();

                int numLabels = Cv2.ConnectedComponentsWithStats(binary, labels, stats, centroids);

                List<SelectionShape> rois = new List<SelectionShape>();
                int objectCount = 1;

                var labelIndexer = labels.GetGenericIndexer<int>();

                // Iterar sobre los objetos encontrados (Empezamos en 1 porque 0 es fondo)
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

                        // Colores aleatorios para zonas ilimitadas
                        var shape = Activator.CreateInstance(Type.GetType("SpecimenFX17.Imaging.MaskShape")!, new object[] { objMask, GetRandomColor() }) as SelectionShape;

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