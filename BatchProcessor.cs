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
        public bool ApplySNV { get; set; }
        public bool ApplyMSC { get; set; }
        public bool ConvertToAbsorbance { get; set; }
    }

    public static class BatchProcessor
    {
        public static async Task ProcessFolderAsync(string inputFolder, string outputCsvPath, BatchOptions options, IProgress<int> progress)
        {
            await Task.Run(() =>
            {
                var hdrFiles = Directory.GetFiles(inputFolder, "*.hdr");
                if (hdrFiles.Length == 0) throw new Exception("No se encontraron archivos .hdr en la carpeta especificada.");

                bool headerWritten = false;

                // BUG 9 SOLUCIONADO: Escritura directa a disco (I/O Streaming) para evitar OutOfMemory en lotes grandes
                using var writer = new StreamWriter(outputCsvPath, false, Encoding.UTF8);

                for (int i = 0; i < hdrFiles.Length; i++)
                {
                    string hdrPath = hdrFiles[i];

                    try
                    {
                        var cube = HyperspectralCube.Load(hdrPath);

                        if (options.ConvertToAbsorbance)
                            cube.ConvertToAbsorbance();
                        if (options.ApplySNV)
                            cube.ApplySNV();
                        if (options.ApplyMSC)
                            cube.ApplyMSC();

                        float[] globalSpectrum = cube.GetGlobalMeanSpectrum();

                        if (!headerWritten)
                        {
                            writer.Write("Filename,");
                            writer.WriteLine(string.Join(",", cube.Header.Wavelengths.Select(w => w.ToString("F2"))));
                            headerWritten = true;
                        }

                        writer.Write($"{Path.GetFileNameWithoutExtension(hdrPath)},");
                        writer.WriteLine(string.Join(",", globalSpectrum.Select(v => v.ToString("G5"))));
                    }
                    catch (Exception ex)
                    {
                        writer.WriteLine($"{Path.GetFileNameWithoutExtension(hdrPath)}, ERROR AL CARGAR O PROCESAR: {ex.Message}");
                    }

                    // Forzar vaciado periódico del buffer al disco
                    if (i % 10 == 0) writer.Flush();

                    int percentComplete = (int)(((float)(i + 1) / hdrFiles.Length) * 100);
                    progress?.Report(percentComplete);
                }

                writer.Flush();
            });
        }
    }
}