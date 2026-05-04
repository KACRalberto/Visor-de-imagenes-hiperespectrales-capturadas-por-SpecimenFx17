using System;
using System.Collections.Generic;
using System.Linq;

namespace SpecimenFX17.Imaging
{
    public class PcaResult
    {
        public double[,] Scores { get; set; } = new double[0, 0];
        public double[,] Loadings { get; set; } = new double[0, 0];
        public double[] ExplainedVariance { get; set; } = Array.Empty<double>();
        public List<string> SampleIds { get; set; } = new();
        public List<string> Classes { get; set; } = new();
        public List<double> Wavelengths { get; set; } = new();
    }

    public static class PcaEngine
    {
        public static PcaResult CalculatePcaFromCsv(string csvPath, int numComponents = 3)
        {
            var lines = System.IO.File.ReadAllLines(csvPath);
            if (lines.Length < 2) throw new Exception("El CSV está vacío o no tiene suficientes datos.");

            var header = lines[0].Split(',');
            var wavelengths = new List<double>();

            // Extraer longitudes de onda de la cabecera (asumiendo formato: ID, Clase, Archivo, 400.1, 402.5, ...)
            for (int i = 3; i < header.Length; i++)
            {
                if (double.TryParse(header[i], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double wl))
                    wavelengths.Add(wl);
                else
                    wavelengths.Add(i); // Fallback: usar índice si falla el parseo
            }

            int numSamples = lines.Length - 1;
            int numVariables = wavelengths.Count;
            double[,] data = new double[numSamples, numVariables];
            List<string> sampleIds = new();
            List<string> classes = new();

            // Leer datos
            for (int i = 1; i < lines.Length; i++)
            {
                var parts = lines[i].Split(',');
                sampleIds.Add(parts[0]);
                classes.Add(parts[1]);

                for (int j = 0; j < numVariables; j++)
                {
                    if (double.TryParse(parts[j + 3], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
                        data[i - 1, j] = val;
                }
            }

            // --- CÁLCULO DE PCA (NIPALS Simplificado / SVD iterativo) ---
            // 1. Centrado medio (Mean Centering)
            double[] means = new double[numVariables];
            for (int j = 0; j < numVariables; j++)
            {
                double sum = 0;
                for (int i = 0; i < numSamples; i++) sum += data[i, j];
                means[j] = sum / numSamples;
                for (int i = 0; i < numSamples; i++) data[i, j] -= means[j];
            }

            // 2. Extraer Componentes
            double[,] scores = new double[numSamples, numComponents];
            double[,] loadings = new double[numVariables, numComponents];
            double[] eigenvalues = new double[numComponents];

            double totalVariance = 0;
            for (int i = 0; i < numSamples; i++)
                for (int j = 0; j < numVariables; j++)
                    totalVariance += data[i, j] * data[i, j];

            double[,] residual = (double[,])data.Clone();

            for (int k = 0; k < numComponents; k++)
            {
                // Inicializar score temporal t con una columna aleatoria (o la primera)
                double[] t = new double[numSamples];
                for (int i = 0; i < numSamples; i++) t[i] = residual[i, 0];

                double[] p = new double[numVariables];
                double t_old_norm = 0;

                for (int iter = 0; iter < 100; iter++) // Iteraciones NIPALS
                {
                    // p = (X' * t) / (t' * t)
                    double t_norm = 0;
                    for (int i = 0; i < numSamples; i++) t_norm += t[i] * t[i];

                    for (int j = 0; j < numVariables; j++)
                    {
                        double sum = 0;
                        for (int i = 0; i < numSamples; i++) sum += residual[i, j] * t[i];
                        p[j] = sum / t_norm;
                    }

                    // Normalizar p
                    double p_norm = 0;
                    for (int j = 0; j < numVariables; j++) p_norm += p[j] * p[j];
                    p_norm = Math.Sqrt(p_norm);
                    for (int j = 0; j < numVariables; j++) p[j] /= p_norm;

                    // t = (X * p) / (p' * p)
                    double new_t_norm = 0;
                    for (int i = 0; i < numSamples; i++)
                    {
                        double sum = 0;
                        for (int j = 0; j < numVariables; j++) sum += residual[i, j] * p[j];
                        t[i] = sum;
                        new_t_norm += t[i] * t[i];
                    }

                    // Convergencia
                    if (Math.Abs(new_t_norm - t_old_norm) < 1e-6) break;
                    t_old_norm = new_t_norm;
                }

                // Guardar Score y Loading
                for (int i = 0; i < numSamples; i++) scores[i, k] = t[i];
                for (int j = 0; j < numVariables; j++) loadings[j, k] = p[j];

                // Deflación: E = X - t * p'
                double compVariance = 0;
                for (int i = 0; i < numSamples; i++)
                {
                    for (int j = 0; j < numVariables; j++)
                    {
                        residual[i, j] -= t[i] * p[j];
                        compVariance += (t[i] * p[j]) * (t[i] * p[j]);
                    }
                }

                eigenvalues[k] = compVariance / totalVariance * 100.0; // Varianza en porcentaje
            }

            return new PcaResult
            {
                Scores = scores,
                Loadings = loadings,
                ExplainedVariance = eigenvalues,
                SampleIds = sampleIds,
                Classes = classes,
                Wavelengths = wavelengths
            };
        }
    }
}