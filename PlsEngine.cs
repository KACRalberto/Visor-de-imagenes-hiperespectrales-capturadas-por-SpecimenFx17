using System;
using System.Linq;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

namespace SpecimenFX17.Imaging
{
    public class PlsModel
    {
        public double[] Coefficients { get; set; } = Array.Empty<double>();
        public double Intercept { get; set; }
        public double R2 { get; set; }
        public double RMSE { get; set; }
        public double[] PredictedY { get; set; } = Array.Empty<double>();
    }

    public static class PlsEngine
    {
        /// <summary>
        /// Entrena un modelo PLS1 (Para una sola variable de respuesta Y, ej: °Brix)
        /// usando el algoritmo SIMPLS/NIPALS adaptado.
        /// </summary>
        /// <param name="xData">Matriz de espectros [muestras, bandas]</param>
        /// <param name="yData">Vector de valores objetivo (Brix) [muestras]</param>
        /// <param name="components">Número de Variables Latentes (Componentes)</param>
        public static PlsModel TrainPLS1(double[,] xData, double[] yData, int components)
        {
            int n = xData.GetLength(0); // Muestras
            int p = xData.GetLength(1); // Bandas (Variables)

            if (n != yData.Length) throw new ArgumentException("El número de muestras en X no coincide con Y.");
            if (components >= p || components >= n) throw new ArgumentException("El número de componentes debe ser menor que las muestras y las bandas.");

            var X = Matrix<double>.Build.DenseOfArray(xData);
            var y = Vector<double>.Build.Dense(yData);

            // 1. Centrado de datos (Mean Centering)
            var xMeans = X.ColumnSums() / n;
            var yMean = y.Sum() / n;

            var Xc = Matrix<double>.Build.Dense(n, p, (i, j) => X[i, j] - xMeans[j]);
            var yc = Vector<double>.Build.Dense(n, i => y[i] - yMean);

            // Matrices para almacenar pesos y cargas
            var W = Matrix<double>.Build.Dense(p, components);
            var P = Matrix<double>.Build.Dense(p, components);
            var Q = Vector<double>.Build.Dense(components);

            var X_k = Xc.Clone();
            var y_k = yc.Clone();

            int actualComponents = 0;

            // 2. Bucle iterativo de componentes latentes
            for (int k = 0; k < components; k++)
            {
                // w = X^T * y
                var w = X_k.TransposeThisAndMultiply(y_k);

                // Normalizar w
                double wNorm = w.L2Norm();
                if (wNorm < 1e-10) break; // Colapso matemático, no queda varianza
                w = w / wNorm;

                // t = X * w
                var t = X_k * w;

                double tNormSq = t.DotProduct(t);
                if (tNormSq < 1e-10) break; // Colapso matemático, salimos.

                // q = y^T * t / (t^T * t)
                double q = y_k.DotProduct(t) / tNormSq;

                // p = X^T * t / (t^T * t)
                var p_vec = X_k.TransposeThisAndMultiply(t) / tNormSq;

                // Guardar
                W.SetColumn(k, w);
                P.SetColumn(k, p_vec);
                Q[k] = q;

                // Deflación (Restar la varianza explicada por este componente)
                // X = X - t * p^T
                X_k = X_k - (t.ToColumnMatrix() * p_vec.ToRowMatrix());
                // y = y - t * q
                y_k = y_k - (t * q);

                actualComponents++;
            }

            // 🛡️ ANTI-COLAPSO: Recortamos las matrices al número real de componentes encontrados
            if (actualComponents == 0)
                throw new Exception("No se pudo extraer ningún componente latente válido. Revisa que los datos no sean todos ceros o constantes.");

            if (actualComponents < components)
            {
                W = W.SubMatrix(0, p, 0, actualComponents);
                P = P.SubMatrix(0, p, 0, actualComponents);
                Q = Q.SubVector(0, actualComponents);
            }

            // 3. Calcular Coeficientes Finales de Regresión (B)
            // B = W * (P^T * W)^-1 * Q
            var PtW = P.Transpose() * W;
            var PtW_inv = PtW.Inverse();
            var B = W * PtW_inv * Q;

            // 4. Calcular Intercepto (Des-centrado)
            double intercept = yMean - xMeans.DotProduct(B);

            // 5. Evaluar Modelo (Predicción sobre datos de entrenamiento)
            var yPred = (X * B) + intercept;

            double sst = yc.DotProduct(yc); // Suma de cuadrados total
            var errors = y - yPred;
            double sse = errors.DotProduct(errors); // Suma de cuadrados del error

            // Prevención de división por cero si las muestras son constantes
            double r2 = sst < 1e-10 ? 0 : 1.0 - (sse / sst);
            double rmse = Math.Sqrt(sse / n);

            return new PlsModel
            {
                Coefficients = B.ToArray(),
                Intercept = intercept,
                R2 = r2,
                RMSE = rmse,
                PredictedY = yPred.ToArray()
            };
        }
    }
}