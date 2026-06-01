using MathNet.Numerics.Data.Matlab;
using MathNet.Numerics.LinearAlgebra;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SpecimenFX17.Imaging
{
    public class BatchOptions
    {
        public bool ApplyNormalize { get; set; }

        // 🛠️ NUEVO: Referencias para calibrar el lote
        public HyperspectralCube? WhiteRef { get; set; }
        public HyperspectralCube? DarkRef { get; set; }

        public bool ConvertToAbsorbance { get; set; }
        public bool ApplySNV { get; set; }
        public bool ApplyMSC { get; set; }
        public bool ApplySavitzkyGolay { get; set; }
        public int SgWindow { get; set; } = 15;
        public int SgPoly { get; set; } = 2;
        public int SgDeriv { get; set; } = 1;
        public bool ApplyMedianFilter { get; set; }

        // --- OPCIONES ULTRAVISOR ---
        public bool AutoSegment { get; set; } = true;
        public int SegmentationBand { get; set; } = 65;
        public bool SaveNpyMasks { get; set; } = true;

        // Parámetros personalizados que vienen de la ventana interactiva
        public SegmentationParams? CustomParams { get; set; }
    }

    public static class BatchProcessor
    {
        private static readonly string[] ReferenceFilePrefixes =
            { "dark", "white", "blank", "reference", "ref_", "dark_", "white_", "blanco", "oscuro" };

        private static bool IsReferenceFile(string hdrPath)
        {
            string name = Path.GetFileNameWithoutExtension(hdrPath).ToLowerInvariant();
            return ReferenceFilePrefixes.Any(prefix => name.StartsWith(prefix));
        }

        private static bool HasBinaryData(string hdrPath)
        {
            string basePath = hdrPath[..^4];
            if (File.Exists(basePath)) return true;
            foreach (var ext in new[] { ".raw", ".img", ".dat", ".bil", ".bip", ".bsq" })
                if (File.Exists(basePath + ext)) return true;
            return false;
        }

        private static void SaveAsNpy(string filePath, bool[,] mask, int lines, int samples)
        {
            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            using var bw = new BinaryWriter(fs);

            bw.Write((byte)0x93);
            bw.Write("NUMPY".ToCharArray());
            bw.Write((byte)0x01);
            bw.Write((byte)0x00);

            string dict = $"{{'descr': '|b1', 'fortran_order': False, 'shape': ({lines}, {samples}), }}";

            int unpaddedLength = 6 + 2 + 2 + dict.Length + 1;
            int padding = 64 - (unpaddedLength % 64);
            dict = dict.PadRight(dict.Length + padding, ' ') + "\n";

            bw.Write((ushort)dict.Length);
            bw.Write(Encoding.ASCII.GetBytes(dict));

            for (int y = 0; y < lines; y++)
            {
                for (int x = 0; x < samples; x++)
                {
                    bw.Write(mask[y, x] ? (byte)1 : (byte)0);
                }
            }
        }

        private static void SaveAsEnvi(string filePath, float[,,] data, List<double> wavelengths)
        {
            int lines = data.GetLength(0);
            int bands = data.GetLength(1);
            int samples = data.GetLength(2);

            string hdrPath = Path.ChangeExtension(filePath, ".hdr");
            string bilPath = Path.ChangeExtension(filePath, ".bil");

            using (StreamWriter sw = new StreamWriter(hdrPath))
            {
                sw.WriteLine("ENVI");
                sw.WriteLine("description = { Cubo segmentado automaticamente por SPECIMEN/UltraVisor }");
                sw.WriteLine($"samples = {samples}");
                sw.WriteLine($"lines = {lines}");
                sw.WriteLine($"bands = {bands}");
                sw.WriteLine("header offset = 0");
                sw.WriteLine("file type = ENVI Standard");
                sw.WriteLine("data type = 4");
                sw.WriteLine("interleave = bil");
                sw.WriteLine("sensor type = Unknown");
                sw.WriteLine("byte order = 0");

                // 🛠️ FIX: Decirle al software ENVI que NaN es "Fondo Transparente"
                sw.WriteLine("data ignore value = NaN");

                sw.WriteLine("wavelength = {");
                sw.WriteLine(string.Join(", ", wavelengths.Select(w => w.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))));
                sw.WriteLine("}");
            }

            using (FileStream fs = new FileStream(bilPath, FileMode.Create))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                for (int l = 0; l < lines; l++)
                {
                    for (int b = 0; b < bands; b++)
                    {
                        for (int s = 0; s < samples; s++)
                        {
                            bw.Write(data[l, b, s]);
                        }
                    }
                }
            }
        }

        private static float[,,] ApplyMaskToCube(HyperspectralCube cube, List<SelectionShape> rois)
        {
            int lines = cube.Lines;
            int samples = cube.Samples;
            int bands = cube.Bands;

            float[,,] segmentedData = new float[lines, bands, samples];
            bool[,] combinedMask = new bool[lines, samples];

            foreach (var roi in rois)
            {
                var m = roi.GetMask(lines, samples);
                for (int y = 0; y < lines; y++)
                    for (int x = 0; x < samples; x++)
                        if (m[y, x]) combinedMask[y, x] = true;
            }

            for (int y = 0; y < lines; y++)
            {
                for (int b = 0; b < bands; b++)
                {
                    for (int x = 0; x < samples; x++)
                    {
                        if (combinedMask[y, x])
                            segmentedData[y, b, x] = cube[b, y, x];
                        else
                            segmentedData[y, b, x] = float.NaN; // 🛠️ FIX: No data (Transparente), NO 0f (Negro)
                    }
                }
            }
            return segmentedData;
        }

        private static void SaveDebugPng(HyperspectralCube cube, List<SelectionShape> rois, int band, string savePath, SegmentationParams? p)
        {
            // 1. Normalizar la banda de referencia a 8-bits (escala de grises)
            using Mat gray8U = AutoSegmenter.NormalizeBandTo8Bit(cube, band, p ?? new SegmentationParams());

            // 2. Convertir a BGR para poder pintar a color
            using Mat colorView = new Mat();
            Cv2.CvtColor(gray8U, colorView, ColorConversionCodes.GRAY2BGR);

            // 3. Crear una máscara combinada a partir de los ROIs detectados
            using Mat combinedMask = Mat.Zeros(cube.Lines, cube.Samples, MatType.CV_8UC1);
            var indexer = combinedMask.GetGenericIndexer<byte>();

            foreach (var roi in rois)
            {
                var m = roi.GetMask(cube.Lines, cube.Samples);
                for (int y = 0; y < cube.Lines; y++)
                {
                    for (int x = 0; x < cube.Samples; x++)
                    {
                        if (m[y, x]) indexer[y, x] = 255;
                    }
                }
            }

            // 4. Pintar la máscara sobre la imagen (Verde fluorescente: B=0, G=255, R=0)
            // Cambia el Scalar(0, 255, 0) a (0, 0, 255) si prefieres que se pinte en Rojo.
            colorView.SetTo(new Scalar(0, 255, 0), combinedMask);

            // 5. Guardar la imagen a disco
            Cv2.ImWrite(savePath, colorView);
        }
        public static async Task ProcessFolderAsync(string inputFolder, string outputFolder, BatchOptions options, IProgress<int>? progress, CancellationToken ct = default)
        {
            await Task.Run(async () =>
            {
                var validExtensions = new[] { ".hdr", ".raw", ".bil" };
                var allFiles = Directory.GetFiles(inputFolder, "*.*")
                    .Where(f => validExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .ToList();

                if (allFiles.Count == 0)
                    throw new Exception("No se encontraron archivos procesables en la carpeta.");

                var uniqueHdrFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var file in allFiles)
                {
                    string dir = Path.GetDirectoryName(file) ?? "";
                    string baseName = Path.GetFileNameWithoutExtension(file);
                    string hdrPath = Path.Combine(dir, baseName + ".hdr");
                    if (File.Exists(hdrPath)) uniqueHdrFiles.Add(hdrPath);
                }

                var hdrFiles = uniqueHdrFiles.Where(f => !IsReferenceFile(f) && HasBinaryData(f)).ToArray();

                if (hdrFiles.Length == 0)
                    throw new Exception("No hay muestras procesables válidas.");

                string csvPath = Path.Combine(outputFolder, "Resultados_Espectros.csv");
                string maskDir = Path.Combine(outputFolder, "Segmented_Masks_NPY");
                string matDir = Path.Combine(outputFolder, "Matlab_Export");
                string enviDir = Path.Combine(outputFolder, "ENVI_Segmented");

                if (options.AutoSegment)
                {
                    if (options.SaveNpyMasks) Directory.CreateDirectory(maskDir);
                    Directory.CreateDirectory(matDir);
                    Directory.CreateDirectory(enviDir);
                }

                var csvBuilder = new StringBuilder();
                bool headerWritten = false;

                for (int i = 0; i < hdrFiles.Length; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    string hdrPath = hdrFiles[i];
                    string baseName = Path.GetFileNameWithoutExtension(hdrPath);

                    try
                    {
                        using var cube = HyperspectralCube.Load(hdrPath);

                        // 🛠️ FIX: CALIBRAR EL CUBO CRUDO ANTES DE SEGMENTARLO!
                        if (options.ApplyNormalize && options.WhiteRef != null && options.DarkRef != null)
                        {
                            cube.Calibrate(options.WhiteRef, options.DarkRef, ct);
                        }

                        if (options.ConvertToAbsorbance && cube.IsCalibrated) cube.ConvertToAbsorbance(ct);
                        if (options.ApplySNV) cube.ApplySNV(ct);
                        else if (options.ApplyMSC) cube.ApplyMSC(ct);
                        if (options.ApplySavitzkyGolay) cube.ApplySavitzkyGolay(options.SgWindow, options.SgPoly, options.SgDeriv, ct);
                        if (options.ApplyMedianFilter) cube.ApplySpatialMedianFilter(3, ct);

                        if (!headerWritten)
                        {
                            if (options.AutoSegment) csvBuilder.Append("Image_Name,Instance_ID,");
                            else csvBuilder.Append("Filename,");

                            csvBuilder.AppendLine(string.Join(",", cube.Header.Wavelengths.Select(w => w.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))));
                            headerWritten = true;
                        }

                        if (options.AutoSegment)
                        {
                            if (options.CustomParams != null)
                            {
                                options.CustomParams.StretchMin = float.NaN;
                                options.CustomParams.StretchMax = float.NaN;
                                options.CustomParams.PointsToRepair.Clear();
                                options.CustomParams.PointsToRemove.Clear();
                            }

                            var rois = await AutoSegmenter.SegmentCubeAsync(cube, options.SegmentationBand, options.CustomParams, null, ct);

                            if (rois.Count == 0)
                            {
                                csvBuilder.AppendLine($"{baseName},NO_OBJECTS_FOUND");
                            }
                            else
                            {
                                if (options.SaveNpyMasks)
                                {
                                    bool[,] combinedMask = new bool[cube.Lines, cube.Samples];
                                    foreach (var roi in rois)
                                    {
                                        var m = roi.GetMask(cube.Lines, cube.Samples);
                                        for (int y = 0; y < cube.Lines; y++)
                                            for (int x = 0; x < cube.Samples; x++)
                                                if (m[y, x]) combinedMask[y, x] = true;
                                    }
                                    string npyPath = Path.Combine(maskDir, $"{baseName}_mask.npy");
                                    SaveAsNpy(npyPath, combinedMask, cube.Lines, cube.Samples);
                                }

                                string matFilePath = Path.Combine(matDir, $"{baseName}.mat");
                                var wvMatrix = Matrix<double>.Build.DenseOfRowArrays(cube.Header.Wavelengths.ToArray());
                                var matlabDict = new List<MatlabMatrix> { MatlabWriter.Pack(wvMatrix, "wvgood") };

                                int roiIdx = 1;
                                foreach (var roi in rois)
                                {
                                    float[] spec = roi.GetSpectrum(cube);
                                    var specMat = Matrix<double>.Build.DenseOfRowArrays(spec.Select(f => (double)f).ToArray());
                                    matlabDict.Add(MatlabWriter.Pack(specMat, $"ROI_{roiIdx}_Spec"));
                                    roiIdx++;
                                }
                                MatlabWriter.Store(matFilePath, matlabDict);

                                float[,,] segmentedCube = ApplyMaskToCube(cube, rois);
                                string enviFilePath = Path.Combine(enviDir, $"{baseName}_segmented");
                                SaveAsEnvi(enviFilePath, segmentedCube, cube.Header.Wavelengths);
                                string debugPngPath = Path.Combine(enviDir, $"{baseName}_debug.png");
                                SaveDebugPng(cube, rois, options.SegmentationBand, debugPngPath, options.CustomParams);
                                foreach (var roi in rois)
                                {
                                    float[] objectSpectrum = roi.GetSpectrum(cube);
                                    csvBuilder.Append($"{baseName},{roi.Variety},");
                                    csvBuilder.AppendLine(string.Join(",", objectSpectrum.Select(v => v.ToString("G5", System.Globalization.CultureInfo.InvariantCulture))));
                                }
                            }
                        }
                        else
                        {
                            float[] globalSpectrum = cube.GetGlobalMeanSpectrum();
                            csvBuilder.Append($"{baseName},");
                            csvBuilder.AppendLine(string.Join(",", globalSpectrum.Select(v => v.ToString("G5", System.Globalization.CultureInfo.InvariantCulture))));
                        }
                    }
                    catch (Exception ex)
                    {
                        csvBuilder.AppendLine($"{baseName},ERROR: {ex.Message}");
                    }

                    progress?.Report((int)(((float)(i + 1) / hdrFiles.Length) * 100));
                }

                File.WriteAllText(csvPath, csvBuilder.ToString(), Encoding.UTF8);

            }, ct);
        }
    }
}