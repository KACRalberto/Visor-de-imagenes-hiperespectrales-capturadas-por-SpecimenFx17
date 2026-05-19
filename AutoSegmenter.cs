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
        private static System.Drawing.Color GetRandomColor()
        {
            var rnd = new Random();
            return System.Drawing.Color.FromArgb(rnd.Next(100, 256), rnd.Next(100, 256), rnd.Next(100, 256));
        }

        public static Mat NormalizeBandTo8Bit(HyperspectralCube cube, int band, SegmentationParams p, CancellationToken ct = default)
        {
            int w = cube.Samples;
            int h = cube.Lines;
            Mat gray8U = new Mat(h, w, MatType.CV_8UC1);

            float minV = p.StretchMin;
            float maxV = p.StretchMax;

            if (float.IsNaN(minV) || float.IsNaN(maxV))
            {
                List<float> vals = new List<float>(w * h);
                for (int y = 0; y < h; y++)
                {
                    if (y % 50 == 0) ct.ThrowIfCancellationRequested(); // Frenos de seguridad
                    for (int x = 0; x < w; x++)
                    {
                        float v = cube[band, y, x];
                        if (!float.IsNaN(v) && !float.IsInfinity(v)) vals.Add(v);
                    }
                }

                vals.Sort();
                minV = 0; maxV = 1;
                if (vals.Count > 0)
                {
                    minV = vals[(int)(vals.Count * 0.02)];
                    maxV = vals[(int)(vals.Count * 0.98)];
                }

                p.StretchMin = minV;
                p.StretchMax = maxV;
            }

            float range = maxV - minV;
            if (range <= 0.00001f) range = 1f;

            var indexer = gray8U.GetGenericIndexer<byte>();
            for (int y = 0; y < h; y++)
            {
                if (y % 50 == 0) ct.ThrowIfCancellationRequested();

                for (int x = 0; x < w; x++)
                {
                    float v = cube[band, y, x];
                    if (float.IsNaN(v) || float.IsInfinity(v))
                        indexer[y, x] = 0;
                    else
                    {
                        float t = Math.Clamp((v - minV) / range, 0f, 1f);
                        indexer[y, x] = (byte)(t * 255);
                    }
                }
            }
            return gray8U;
        }

        public static Mat GetRawMask(Mat gray8U, SegmentationParams p)
        {
            Mat binary = new Mat();
            Cv2.Threshold(gray8U, binary, p.Threshold, 255, p.InvertThreshold ? ThresholdTypes.BinaryInv : ThresholdTypes.Binary);

            // Cortar etiquetas de los bordes con seguridad
            if (p.IgnoreTopPct > 0)
            {
                int topRows = (int)(binary.Rows * (p.IgnoreTopPct / 100f));
                if (topRows > 0) binary.RowRange(0, topRows).SetTo(new Scalar(0));
            }
            if (p.IgnoreBottomPct > 0)
            {
                int bottomRows = (int)(binary.Rows * (p.IgnoreBottomPct / 100f));
                if (bottomRows > 0) binary.RowRange(binary.Rows - bottomRows, binary.Rows).SetTo(new Scalar(0));
            }

            if (p.IgnoreLeftPct > 0)
            {
                int leftCols = (int)(binary.Cols * (p.IgnoreLeftPct / 100f));
                if (leftCols > 0) binary.ColRange(0, leftCols).SetTo(new Scalar(0));
            }
            if (p.IgnoreRightPct > 0)
            {
                int rightCols = (int)(binary.Cols * (p.IgnoreRightPct / 100f));
                if (rightCols > 0) binary.ColRange(binary.Cols - rightCols, binary.Cols).SetTo(new Scalar(0));
            }

            Mat kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(5, 5));
            if (p.CloseIters > 0) Cv2.MorphologyEx(binary, binary, MorphTypes.Close, kernel, iterations: p.CloseIters);
            if (p.OpenIters > 0) Cv2.MorphologyEx(binary, binary, MorphTypes.Open, kernel, iterations: p.OpenIters);

            if (p.PointsToRepair != null && p.PointsToRepair.Count > 0)
            {
                foreach (var pt in p.PointsToRepair)
                    Cv2.Circle(binary, new OpenCvSharp.Point(pt.X, pt.Y), 10, new Scalar(255), -1);
            }

            if (p.PointsToRemove != null && p.PointsToRemove.Count > 0)
            {
                foreach (var pt in p.PointsToRemove)
                {
                    if (pt.X >= 0 && pt.X < binary.Cols && pt.Y >= 0 && pt.Y < binary.Rows)
                    {
                        if (binary.At<byte>(pt.Y, pt.X) > 0)
                            Cv2.FloodFill(binary, new OpenCvSharp.Point(pt.X, pt.Y), new Scalar(0));
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
                progress?.Report(10);
                ct.ThrowIfCancellationRequested();

                using Mat gray8U = NormalizeBandTo8Bit(cube, targetBand, p, ct);
                progress?.Report(40);
                ct.ThrowIfCancellationRequested();

                using Mat binary = GetRawMask(gray8U, p);
                progress?.Report(70);
                ct.ThrowIfCancellationRequested();

                using Mat labels = new Mat();
                using Mat stats = new Mat();
                using Mat centroids = new Mat();

                int numLabels = Cv2.ConnectedComponentsWithStats(binary, labels, stats, centroids);

                List<SelectionShape> rois = new List<SelectionShape>();
                int objectCount = 1;
                var labelIndexer = labels.GetGenericIndexer<int>();

                for (int i = 1; i < numLabels; i++)
                {
                    int area = stats.At<int>(i, (int)ConnectedComponentsTypes.Area);
                    if (area >= p.MinArea)
                    {
                        bool[,] objMask = new bool[cube.Lines, cube.Samples];
                        for (int y = 0; y < cube.Lines; y++)
                        {
                            for (int x = 0; x < cube.Samples; x++)
                                if (labelIndexer[y, x] == i) objMask[y, x] = true;
                        }

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