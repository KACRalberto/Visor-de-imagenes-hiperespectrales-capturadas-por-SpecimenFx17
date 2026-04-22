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
        public bool SaveNpyMasks { get; set; } = true; // Guarda las máscaras en formato .npy (NumPy)
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

        /// <summary>
        /// Genera un archivo binario .npy (NumPy Array) compatible con Python, 
        /// conteniendo la matriz booleana (True = Objeto, False = Fondo).
        /// </summary>
        private static void SaveAsNpy(string filePath, bool[,] mask, int lines, int samples)
        {
            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            using var bw = new BinaryWriter(fs);

            // 1. Cabecera Mágica de NumPy
            bw.Write((byte)0x93);
            bw.Write("NUMPY".ToCharArray());
            bw.Write((byte)0x01); // Version Major
            bw.Write((byte)0x00); // Version Minor

            // 2. Diccionario de metadatos del array
            // '|b1' significa boolean (1 byte). lines = filas (Y), samples = columnas (X)
            string dict = $"{{'descr': '|b1', 'fortran_order': False, 'shape': ({lines}, {samples}), }}";

            // 3. Relleno (padding) para que la cabecera total sea múltiplo de 64 bytes
            int unpaddedLength = 6 + 2 + 2 + dict.Length + 1; // Magic + Version + HeaderLen + Dict + '\n'
            int padding = 64 - (unpaddedLength % 64);
            dict = dict.PadRight(dict.Length + padding, ' ') + "\n";

            bw.Write((ushort)dict.Length);
            bw.Write(Encoding.ASCII.GetBytes(dict));

            // 4. Volcado de datos binarios (1 byte por píxel: 0 o 1)
            for (int y = 0; y < lines; y++)
            {
                for (int x = 0; x < samples; x++)
                {
                    bw.Write(mask[y, x] ? (byte)1 : (byte)0);
                }
            }
        }

        public static async Task ProcessFolderAsync(string inputFolder, string outputCsvPath, BatchOptions options, IProgress<int>? progress, CancellationToken ct = default)
        {
            await Task.Run(async () =>
            {
                var validExtensions = new[] { ".hdr", ".raw", ".bil" };
                var allFiles = Directory.GetFiles(inputFolder, "*.*")
                    .Where(f => validExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .ToList();

                if (allFiles.Count == 0)
                    throw new Exception("No se encontraron archivos .hdr, .raw o .bil en la carpeta.");

                var uniqueHdrFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var file in allFiles)
                {
                    string dir = Path.GetDirectoryName(file) ?? "";
                    string baseName = Path.GetFileNameWithoutExtension(file);
                    string hdrPath = Path.Combine(dir, baseName + ".hdr");
                    if (File.Exists(hdrPath)) uniqueHdrFiles.Add(hdrPath);
                }

                var allHdrFilesArray = uniqueHdrFiles.ToArray();
                if (allHdrFilesArray.Length == 0)
                    throw new Exception("No se encontraron cabeceras (.hdr).");

                var skippedRef = allHdrFilesArray.Where(IsReferenceFile).ToList();
                var skippedNoBin = allHdrFilesArray.Where(f => !IsReferenceFile(f) && !HasBinaryData(f)).ToList();
                var hdrFiles = allHdrFilesArray.Where(f => !IsReferenceFile(f) && HasBinaryData(f)).ToArray();

                if (hdrFiles.Length == 0)
                    throw new Exception($"No hay muestras procesables. Omitidos ref: {skippedRef.Count}, Sin binario: {skippedNoBin.Count}");

                var csvBuilder = new StringBuilder();
                bool headerWritten = false;

                string? outputDir = Path.GetDirectoryName(outputCsvPath);
                string maskDir = Path.Combine(outputDir ?? inputFolder, "Segmented_Masks_NPY");

                for (int i = 0; i < hdrFiles.Length; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    string hdrPath = hdrFiles[i];
                    string baseName = Path.GetFileNameWithoutExtension(hdrPath);

                    try
                    {
                        using var cube = HyperspectralCube.Load(hdrPath);

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
                            var rois = await AutoSegmenter.SegmentCubeAsync(cube, options.SegmentationBand, null, ct);

                            if (rois.Count == 0)
                            {
                                csvBuilder.AppendLine($"{baseName},NO_OBJECTS_FOUND");
                            }
                            else
                            {
                                // --- GUARDADO EXACTO AL SCRIPT DE PYTHON (MÁSCARA LÓGICA EN .NPY) ---
                                if (options.SaveNpyMasks)
                                {
                                    if (!Directory.Exists(maskDir)) Directory.CreateDirectory(maskDir);

                                    // Combinamos todas las instancias en una sola máscara lógica general (> 0)
                                    bool[,] combinedMask = new bool[cube.Lines, cube.Samples];
                                    foreach (var roi in rois)
                                    {
                                        var m = roi.GetMask(cube.Lines, cube.Samples);
                                        for (int y = 0; y < cube.Lines; y++)
                                        {
                                            for (int x = 0; x < cube.Samples; x++)
                                            {
                                                if (m[y, x]) combinedMask[y, x] = true;
                                            }
                                        }
                                    }

                                    string npyPath = Path.Combine(maskDir, $"{baseName}_mask.npy");
                                    SaveAsNpy(npyPath, combinedMask, cube.Lines, cube.Samples);
                                }

                                // --- EXTRACCIÓN DE ESPECTROS AL CSV ---
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

                File.WriteAllText(outputCsvPath, csvBuilder.ToString(), Encoding.UTF8);

            }, ct);
        }
    }
}