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
        /// Aplica un algoritmo de Computer Vision (Otsu + Morfología + Watershed) para detectar 
        /// y separar automáticamente objetos en el cubo hiperespectral.
        /// </summary>
        /// <param name="cube">El cubo hiperespectral cargado.</param>
        /// <param name="targetBand">La banda de alto contraste (ej. infrarrojo cercano o rojo) a usar para segmentar.</param>
        public static async Task<List<SelectionShape>> SegmentCubeAsync(HyperspectralCube cube, int targetBand = 65, IProgress<int>? progress = null, CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                int w = cube.Samples;
                int h = cube.Lines;

                // 1. Extraer la banda y normalizar a 8-bits (0-255) para OpenCV
                progress?.Report(10);
                ct.ThrowIfCancellationRequested();

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

                // 2. Umbralización (Threshold de Otsu)
                progress?.Report(30);
                ct.ThrowIfCancellationRequested();
                using Mat binary = new Mat();
                Cv2.Threshold(gray8U, binary, 0, 255, ThresholdTypes.Otsu | ThresholdTypes.Binary);

                // --- CORRECCIÓN ULTRAVISOR: Detección automática de fondo brillante ---
                // Si la cinta refleja más que la muestra, el fondo será blanco (255) y la muestra negra (0).
                // Comprobamos los píxeles en el perímetro de la imagen para saber si hay que invertir la máscara.
                int edgeWhitePixels = 0;
                int totalEdgePixels = (w * 2) + (h * 2) - 4;
                var binIndexer = binary.GetGenericIndexer<byte>();

                for (int x = 0; x < w; x++)
                {
                    if (binIndexer[0, x] == 255) edgeWhitePixels++;
                    if (binIndexer[h - 1, x] == 255) edgeWhitePixels++;
                }
                for (int y = 1; y < h - 1; y++)
                {
                    if (binIndexer[y, 0] == 255) edgeWhitePixels++;
                    if (binIndexer[y, w - 1] == 255) edgeWhitePixels++;
                }

                // Si más del 50% de los bordes son blancos, el fondo está invertido. ¡Lo arreglamos!
                if (edgeWhitePixels > totalEdgePixels * 0.5)
                {
                    Cv2.BitwiseNot(binary, binary);
                }
                // ----------------------------------------------------------------------

                // 3. Morfología (Limpiar ruido exterior y rellenar huecos interiores)
                progress?.Report(50);
                using Mat kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(5, 5));
                using Mat clean = new Mat();

                Cv2.MorphologyEx(binary, clean, MorphTypes.Open, kernel, iterations: 2);
                Cv2.MorphologyEx(clean, clean, MorphTypes.Close, kernel, iterations: 2);

                // 4. Transformada de Distancia y Watershed (Separar muestras pegadas)
                progress?.Report(70);
                ct.ThrowIfCancellationRequested();

                using Mat sureBg = new Mat();
                Cv2.Dilate(clean, sureBg, kernel, iterations: 3);

                using Mat distTransform = new Mat();
                Cv2.DistanceTransform(clean, distTransform, DistanceTypes.L2, DistanceTransformMasks.Mask5);

                using Mat sureFg = new Mat();
                double distMin, distMax;
                Cv2.MinMaxLoc(distTransform, out distMin, out distMax);
                Cv2.Threshold(distTransform, sureFg, 0.4 * distMax, 255, ThresholdTypes.Binary);
                sureFg.ConvertTo(sureFg, MatType.CV_8UC1);

                using Mat unknown = new Mat();
                Cv2.Subtract(sureBg, sureFg, unknown);

                using Mat markers = new Mat();
                Cv2.ConnectedComponents(sureFg, markers);

                var markerIndexer = markers.GetGenericIndexer<int>();
                var unknownIndexer = unknown.GetGenericIndexer<byte>();

                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        markerIndexer[y, x] += 1;
                        if (unknownIndexer[y, x] == 255)
                        {
                            markerIndexer[y, x] = 0;
                        }
                    }
                }

                using Mat rgbDummy = new Mat();
                Cv2.CvtColor(gray8U, rgbDummy, ColorConversionCodes.GRAY2BGR);

                Cv2.Watershed(rgbDummy, markers);

                // 5. Extraer y empaquetar en ROIs nativos de Specimen
                progress?.Report(90);
                ct.ThrowIfCancellationRequested();

                HashSet<int> uniqueLabels = new HashSet<int>();
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int label = markerIndexer[y, x];
                        if (label > 1) uniqueLabels.Add(label);
                    }
                }

                List<SelectionShape> rois = new List<SelectionShape>();
                Random rnd = new Random();

                int objectCount = 1;
                foreach (int label in uniqueLabels)
                {
                    bool[,] mask = new bool[h, w];
                    bool hasPixels = false;

                    for (int y = 0; y < h; y++)
                    {
                        for (int x = 0; x < w; x++)
                        {
                            if (markerIndexer[y, x] == label)
                            {
                                mask[y, x] = true;
                                hasPixels = true;
                            }
                        }
                    }

                    if (hasPixels)
                    {
                        System.Drawing.Color c = System.Drawing.Color.FromArgb(rnd.Next(50, 255), rnd.Next(50, 255), rnd.Next(50, 255));

                        var shape = Activator.CreateInstance(Type.GetType("SpecimenFX17.Imaging.MaskShape")!, new object[] { mask, c }) as SelectionShape;
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