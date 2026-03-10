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
        /// <summary>
        /// Procesa todos los archivos .hdr de una carpeta, aplica filtros quimiométricos 
        /// y extrae el espectro medio a un único archivo CSV resumen.
        /// </summary>
        public static async Task ProcessFolderAsync(string inputFolder, string outputCsvPath, BatchOptions options, IProgress<int> progress)
        {
            await Task.Run(() =>
            {
                var hdrFiles = Directory.GetFiles(inputFolder, "*.hdr");
                if (hdrFiles.Length == 0) throw new Exception("No se encontraron archivos .hdr en la carpeta especificada.");

                var csvBuilder = new StringBuilder();
                bool headerWritten = false;

                for (int i = 0; i < hdrFiles.Length; i++)
                {
                    string hdrPath = hdrFiles[i];

                    try
                    {
                        // 1. Cargar el cubo hiperdimensional
                        var cube = HyperspectralCube.Load(hdrPath);

                        // 2. Aplicar Preprocesamiento si el usuario lo solicita
                        if (options.ConvertToAbsorbance)
                            cube.ConvertToAbsorbance();

                        if (options.ApplySNV)
                            cube.ApplySNV();

                        if (options.ApplyMSC)
                            cube.ApplyMSC();

                        // 3. Extraer el espectro medio global de toda la imagen
                        float[] globalSpectrum = cube.GetGlobalMeanSpectrum();

                        // 4. Escribir la cabecera (longitudes de onda) en la primera iteración
                        if (!headerWritten)
                        {
                            csvBuilder.Append("Filename,");
                            csvBuilder.AppendLine(string.Join(",", cube.Header.Wavelengths.Select(w => w.ToString("F2"))));
                            headerWritten = true;
                        }

                        // 5. Escribir los datos de este archivo como una nueva fila en el Excel/CSV
                        csvBuilder.Append($"{Path.GetFileNameWithoutExtension(hdrPath)},");
                        csvBuilder.AppendLine(string.Join(",", globalSpectrum.Select(v => v.ToString("G5"))));
                    }
                    catch (Exception ex)
                    {
                        // Si un archivo está corrupto, lo apuntamos en el CSV pero el bucle sigue vivo
                        csvBuilder.AppendLine($"{Path.GetFileNameWithoutExtension(hdrPath)}, ERROR AL CARGAR O PROCESAR: {ex.Message}");
                    }

                    // Reportar progreso (0 a 100%) a la UI
                    int percentComplete = (int)(((float)(i + 1) / hdrFiles.Length) * 100);
                    progress?.Report(percentComplete);
                }

                // Guardar la tabla matriz final en el disco
                File.WriteAllText(outputCsvPath, csvBuilder.ToString(), Encoding.UTF8);
            });
        }
    }
}