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
                if (!Directory.Exists(inputFolder))
                    throw new DirectoryNotFoundException("La carpeta de entrada no existe.");

                var hdrFiles = Directory.GetFiles(inputFolder, "*.hdr");
                if (hdrFiles.Length == 0)
                    throw new Exception("No se encontraron archivos .hdr en la carpeta especificada.");

                bool headerWritten = false;

                // BUG 2.3 SOLUCIONADO: Manejo robusto de excepciones de I/O si el CSV está abierto en otro programa
                try
                {
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
                                var wls = cube.Header.Wavelengths;
                                // BUG 2.2 SOLUCIONADO: Prevención si Wavelengths es null
                                if (wls != null && wls.Count == cube.Bands)
                                    writer.WriteLine(string.Join(",", wls.Select(w => w.ToString("F2"))));
                                else
                                    writer.WriteLine(string.Join(",", Enumerable.Range(1, cube.Bands).Select(b => $"Band_{b}")));

                                headerWritten = true;
                            }

                            writer.Write($"{Path.GetFileNameWithoutExtension(hdrPath)},");
                            writer.WriteLine(string.Join(",", globalSpectrum.Select(v => v.ToString("G5"))));
                        }
                        catch (Exception ex)
                        {
                            // Si falla un solo cubo, documentamos el error pero no crasheamos el lote completo
                            writer.WriteLine($"{Path.GetFileNameWithoutExtension(hdrPath)}, ERROR AL CARGAR O PROCESAR: {ex.Message}");
                        }

                        // Forzar vaciado periódico del buffer al disco para evitar sobrecarga RAM
                        if (i % 10 == 0) writer.Flush();

                        int percentComplete = (int)(((float)(i + 1) / hdrFiles.Length) * 100);
                        progress?.Report(percentComplete);
                    }

                    writer.Flush();
                }
                catch (IOException ioEx)
                {
                    throw new Exception($"No se pudo escribir en el archivo de salida. Asegúrate de que no esté abierto en Excel u otro programa.\nDetalles: {ioEx.Message}");
                }
            });
        }
    }
}