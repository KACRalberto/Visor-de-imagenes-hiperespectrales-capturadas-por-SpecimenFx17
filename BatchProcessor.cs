using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SpecimenFX17.Imaging
{
    public class BatchOptions
    {
        public bool ApplyNormalize { get; set; }  // Calibración blanco/negro
        public bool ConvertToAbsorbance { get; set; }  // -log(R), requiere normalización
        public bool ApplySNV { get; set; }  // SNV (excluyente con MSC)
        public bool ApplyMSC { get; set; }  // MSC (excluyente con SNV)
        public bool ApplySavitzkyGolay { get; set; }
        public int SgWindow { get; set; } = 15;
        public int SgPoly { get; set; } = 2;
        public int SgDeriv { get; set; } = 1;
        public bool ApplyMedianFilter { get; set; }
    }

    public static class BatchProcessor
    {
        // Prefijos de nombre que identifican archivos de referencia (no son muestras)
        private static readonly string[] ReferenceFilePrefixes =
            { "dark", "white", "blank", "reference", "ref_", "dark_", "white_", "blanco", "oscuro" };

        /// <summary>
        /// Devuelve true si el .hdr corresponde a un archivo de referencia (blanco/oscuro)
        /// que no debe procesarse como muestra.
        /// </summary>
        private static bool IsReferenceFile(string hdrPath)
        {
            string name = Path.GetFileNameWithoutExtension(hdrPath).ToLowerInvariant();
            return ReferenceFilePrefixes.Any(prefix => name.StartsWith(prefix));
        }

        /// <summary>
        /// Devuelve true si existe el archivo binario de datos (.raw, .bil, .bip, etc.)
        /// </summary>
        private static bool HasBinaryData(string hdrPath)
        {
            string basePath = hdrPath[..^4]; // quitar .hdr
            if (File.Exists(basePath)) return true;
            foreach (var ext in new[] { ".raw", ".img", ".dat", ".bil", ".bip", ".bsq" })
                if (File.Exists(basePath + ext)) return true;
            return false;
        }

        /// <summary>
        /// Procesa todos los archivos .hdr, .raw y .bil de una carpeta, aplica filtros quimiométricos 
        /// y extrae el espectro medio a un único archivo CSV resumen.
        /// Omite automáticamente archivos de referencia (dark/white) y archivos sin binario.
        /// </summary>
        public static async Task ProcessFolderAsync(string inputFolder, string outputCsvPath, BatchOptions options, IProgress<int> progress)
        {
            await Task.Run(() =>
            {
                // Buscar archivos con extensiones soportadas
                var validExtensions = new[] { ".hdr", ".raw", ".bil" };
                var allFiles = Directory.GetFiles(inputFolder, "*.*")
                    .Where(f => validExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .ToList();

                if (allFiles.Count == 0)
                    throw new Exception("No se encontraron archivos .hdr, .raw o .bil en la carpeta especificada.");

                // Usamos un HashSet para agrupar las muestras únicas por su archivo .hdr
                // Esto evita procesar la muestra 2 veces si el usuario tiene "muestra.hdr" y "muestra.raw" en la carpeta
                var uniqueHdrFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var file in allFiles)
                {
                    string dir = Path.GetDirectoryName(file) ?? "";
                    string baseName = Path.GetFileNameWithoutExtension(file);
                    string hdrPath = Path.Combine(dir, baseName + ".hdr");

                    // Validar que el archivo .hdr exista, ya que es mandatorio para leer formato ENVI
                    if (File.Exists(hdrPath))
                    {
                        uniqueHdrFiles.Add(hdrPath);
                    }
                }

                var allHdrFilesArray = uniqueHdrFiles.ToArray();

                if (allHdrFilesArray.Length == 0)
                    throw new Exception("No se encontraron archivos de cabecera (.hdr) asociados a los archivos de datos.");

                // Filtrar: solo muestras con archivo binario presente
                var skippedRef = allHdrFilesArray.Where(IsReferenceFile).ToList();
                var skippedNoBin = allHdrFilesArray.Where(f => !IsReferenceFile(f) && !HasBinaryData(f)).ToList();
                var hdrFiles = allHdrFilesArray.Where(f => !IsReferenceFile(f) && HasBinaryData(f)).ToArray();

                if (hdrFiles.Length == 0)
                    throw new Exception(
                        $"No hay muestras procesables.\n" +
                        $"  - Omitidos por ser referencia (dark/white): {skippedRef.Count}\n" +
                        $"  - Omitidos por falta de archivo binario: {skippedNoBin.Count}");

                var csvBuilder = new StringBuilder();

                // Línea de diagnóstico: pipeline aplicado
                var pipelineSteps = new System.Collections.Generic.List<string> { "Raw" };
                if (options.ConvertToAbsorbance) pipelineSteps.Add("Absorbancia");
                if (options.ApplySNV) pipelineSteps.Add("SNV");
                if (options.ApplyMSC) pipelineSteps.Add("MSC");
                if (options.ApplySavitzkyGolay) pipelineSteps.Add($"SG(W:{options.SgWindow},P:{options.SgPoly},D:{options.SgDeriv})");
                if (options.ApplyMedianFilter) pipelineSteps.Add("Mediana3x3");
                csvBuilder.AppendLine($"# Pipeline aplicado: {string.Join(" → ", pipelineSteps)}");

                // Cabecera de diagnóstico: archivos omitidos
                if (skippedRef.Count > 0)
                    csvBuilder.AppendLine($"# OMITIDOS (referencia dark/white): {string.Join(", ", skippedRef.Select(Path.GetFileNameWithoutExtension))}");
                if (skippedNoBin.Count > 0)
                    csvBuilder.AppendLine($"# OMITIDOS (sin archivo binario): {string.Join(", ", skippedNoBin.Select(Path.GetFileNameWithoutExtension))}");

                bool headerWritten = false;

                for (int i = 0; i < hdrFiles.Length; i++)
                {
                    string hdrPath = hdrFiles[i];

                    try
                    {
                        var cube = HyperspectralCube.Load(hdrPath);

                        // Pipeline en el mismo orden que la interfaz principal
                        if (options.ConvertToAbsorbance && cube.IsCalibrated)
                            cube.ConvertToAbsorbance();

                        if (options.ApplySNV)
                            cube.ApplySNV();
                        else if (options.ApplyMSC)
                            cube.ApplyMSC();

                        if (options.ApplySavitzkyGolay)
                            cube.ApplySavitzkyGolay(options.SgWindow, options.SgPoly, options.SgDeriv);

                        if (options.ApplyMedianFilter)
                            cube.ApplySpatialMedianFilter(3);

                        float[] globalSpectrum = cube.GetGlobalMeanSpectrum();

                        if (!headerWritten)
                        {
                            csvBuilder.Append("Filename,");
                            csvBuilder.AppendLine(string.Join(",", cube.Header.Wavelengths.Select(w => w.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))));
                            headerWritten = true;
                        }

                        csvBuilder.Append($"{Path.GetFileNameWithoutExtension(hdrPath)},");
                        csvBuilder.AppendLine(string.Join(",", globalSpectrum.Select(v => v.ToString("G5", System.Globalization.CultureInfo.InvariantCulture))));
                    }
                    catch (Exception ex)
                    {
                        csvBuilder.AppendLine($"{Path.GetFileNameWithoutExtension(hdrPath)}, ERROR: {ex.Message}");
                    }

                    progress?.Report((int)(((float)(i + 1) / hdrFiles.Length) * 100));
                }

                File.WriteAllText(outputCsvPath, csvBuilder.ToString(), Encoding.UTF8);
            });
        }
    }
}