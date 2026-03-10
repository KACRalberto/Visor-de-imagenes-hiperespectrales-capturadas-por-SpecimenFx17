using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace SpecimenFX17.Imaging
{
    public class HyperspectralCube
    {
        public EnviHeader Header { get; }
        public int Bands => _cube.GetLength(0);
        public int Lines => Header.Lines;
        public int Samples => Header.Samples;

        private readonly float[,,] _cube;
        public float GlobalMin { get; private set; }
        public float GlobalMax { get; private set; }
        public bool IsCalibrated { get; private set; } = false;
        public bool IsAbsorbance { get; private set; } = false;

        public HyperspectralCube(EnviHeader header, float[,,] cube)
        {
            Header = header;
            _cube = cube;
            ComputeStats();
        }

        public float this[int band, int line, int sample] => _cube[band, line, sample];

        public float[,] GetBand(int bandIndex)
        {
            if (bandIndex < 0 || bandIndex >= Bands) throw new ArgumentOutOfRangeException(nameof(bandIndex));
            var img = new float[Lines, Samples];
            for (int l = 0; l < Lines; l++)
                for (int s = 0; s < Samples; s++)
                    img[l, s] = _cube[bandIndex, l, s];
            return img;
        }

        public float[] GetSpectrum(int line, int sample)
        {
            var spec = new float[Bands];
            for (int b = 0; b < Bands; b++) spec[b] = _cube[b, line, sample];
            return spec;
        }

        public (float Min, float Max) GetBandStats(int bandIndex)
        {
            float min = float.MaxValue, max = float.MinValue;
            float ignore = (float)Header.DataIgnoreValue;

            for (int l = 0; l < Lines; l++)
                for (int s = 0; s < Samples; s++)
                {
                    float v = _cube[bandIndex, l, s];
                    if (float.IsNaN(v) || Math.Abs(v - ignore) < 1e-5) continue;
                    if (v < min) min = v;
                    if (v > max) max = v;
                }

            return (min == float.MaxValue ? 0f : min, max == float.MinValue ? 1f : max);
        }

        // --- NORMALIZACIÓN EXACTA AL PROGRAMA HYPER.CS ---
        public void Calibrate(HyperspectralCube whiteRef, HyperspectralCube darkRef)
        {
            if (whiteRef.Bands != Bands || darkRef.Bands != Bands) throw new Exception("El número de bandas no coincide.");
            if (whiteRef.Samples != Samples || darkRef.Samples != Samples) throw new Exception("El número de columnas no coincide.");

            float[,] maxWhite = new float[Bands, Samples];
            float[,] minDark = new float[Bands, Samples];

            Parallel.For(0, Bands, b =>
            {
                for (int s = 0; s < Samples; s++)
                {
                    float wMax = float.MinValue;
                    float dMin = float.MaxValue;

                    // Buscar el valor máximo absoluto para la referencia blanca
                    for (int l = 0; l < whiteRef.Lines; l++)
                    {
                        float v = whiteRef[b, l, s];
                        if (!float.IsNaN(v) && v > wMax) wMax = v;
                    }
                    // Buscar el valor mínimo absoluto para la referencia oscura
                    for (int l = 0; l < darkRef.Lines; l++)
                    {
                        float v = darkRef[b, l, s];
                        if (!float.IsNaN(v) && v < dMin) dMin = v;
                    }

                    maxWhite[b, s] = wMax == float.MinValue ? 1f : wMax;
                    minDark[b, s] = dMin == float.MaxValue ? 0f : dMin;
                }
            });

            Parallel.For(0, Bands, b =>
            {
                for (int l = 0; l < Lines; l++)
                {
                    for (int s = 0; s < Samples; s++)
                    {
                        float val = _cube[b, l, s];
                        if (float.IsNaN(val)) continue;

                        float w = maxWhite[b, s];
                        float d = minDark[b, s];

                        // Saturación idéntica a Hyper.cs para evitar artefactos
                        if (val > w)
                        {
                            _cube[b, l, s] = 1.0f;
                        }
                        else if (val < d)
                        {
                            _cube[b, l, s] = 0f;
                        }
                        else
                        {
                            float range = w - d;
                            if (range <= 0.0001f) range = 0.0001f;
                            _cube[b, l, s] = (val - d) / range;
                        }
                    }
                }
            });

            IsCalibrated = true;
            IsAbsorbance = false;
            ComputeStats();
        }

        public void ConvertToAbsorbance()
        {
            if (!IsCalibrated || IsAbsorbance) return;

            Parallel.For(0, Bands, b =>
            {
                for (int l = 0; l < Lines; l++)
                {
                    for (int s = 0; s < Samples; s++)
                    {
                        float r = _cube[b, l, s];
                        if (float.IsNaN(r)) continue;

                        if (r <= 0.0001f) r = 0.0001f;
                        _cube[b, l, s] = (float)-Math.Log10(r);
                    }
                }
            });

            IsAbsorbance = true;
            ComputeStats();
        }

        public void ApplySpatialMedianFilter(int kernelSize)
        {
            if (kernelSize <= 1) return;
            int offset = kernelSize / 2;

            float[,,] newCube = new float[Bands, Lines, Samples];

            Parallel.For(0, Bands, b =>
            {
                float[] window = new float[kernelSize * kernelSize];

                for (int l = 0; l < Lines; l++)
                {
                    for (int s = 0; s < Samples; s++)
                    {
                        int count = 0;
                        for (int dy = -offset; dy <= offset; dy++)
                        {
                            int yy = Math.Clamp(l + dy, 0, Lines - 1);
                            for (int dx = -offset; dx <= offset; dx++)
                            {
                                int xx = Math.Clamp(s + dx, 0, Samples - 1);
                                window[count++] = _cube[b, yy, xx];
                            }
                        }
                        Array.Sort(window, 0, count);
                        newCube[b, l, s] = window[count / 2];
                    }
                }
            });

            Array.Copy(newCube, _cube, newCube.Length);
            ComputeStats();
        }

        // --- NUEVA FUNCIÓN: ANÁLISIS MASIVO CON COMPORTAMIENTO IDÉNTICO A HYPER.CS ---
        public HyperspectralCube GenerateAnalyzedCube(int numPca = 10, bool[,] mask = null)
        {
            int origBands = Header.Bands;
            if (mask == null)
            {
                mask = new bool[Lines, Samples];
                for (int l = 0; l < Lines; l++) for (int s = 0; s < Samples; s++) mask[l, s] = true;
            }

            int newBands = origBands + 4 + numPca; // Originales + Media + Min + Max + Rango + PCAs
            float[,,] newCube = new float[newBands, Lines, Samples];

            // 1. Copiar bandas originales y calcular Media, Min, Max, Rango
            Parallel.For(0, Lines, y => {
                for (int x = 0; x < Samples; x++)
                {
                    float min = float.MaxValue, max = float.MinValue, sum = 0;
                    int valid = 0;

                    if (!mask[y, x] || float.IsNaN(_cube[0, y, x]))
                    {
                        for (int b = 0; b < origBands; b++) newCube[b, y, x] = _cube[b, y, x];
                        newCube[origBands, y, x] = float.NaN;
                        newCube[origBands + 1, y, x] = float.NaN;
                        newCube[origBands + 2, y, x] = float.NaN;
                        newCube[origBands + 3, y, x] = float.NaN;
                        continue;
                    }

                    for (int b = 0; b < origBands; b++)
                    {
                        float v = _cube[b, y, x];
                        newCube[b, y, x] = v;
                        if (!float.IsNaN(v))
                        {
                            if (v < min) min = v;
                            if (v > max) max = v;
                            sum += v;
                            valid++;
                        }
                    }

                    if (valid > 0)
                    {
                        newCube[origBands, y, x] = sum / valid;      // Media
                        newCube[origBands + 1, y, x] = min;          // Mínima
                        newCube[origBands + 2, y, x] = max;          // Máxima
                        newCube[origBands + 3, y, x] = max - min;    // Rango
                    }
                }
            });

            // 2. Setup PCA
            int step = 2;
            var mean = new double[origBands];
            int n = 0;
            object syncObj = new object();

            Parallel.For(0, (Lines + step - 1) / step, rowIdx => {
                int y = rowIdx * step;
                double[] localMean = new double[origBands];
                int localN = 0;
                for (int x = 0; x < Samples; x += step)
                {
                    if (!mask[y, x] || float.IsNaN(_cube[0, y, x])) continue;
                    for (int b = 0; b < origBands; b++) localMean[b] += _cube[b, y, x];
                    localN++;
                }
                lock (syncObj)
                {
                    for (int b = 0; b < origBands; b++) mean[b] += localMean[b];
                    n += localN;
                }
            });

            if (n > 1)
            {
                for (int b = 0; b < origBands; b++) mean[b] /= n;

                var cov = new double[origBands, origBands];
                Parallel.For(0, (Lines + step - 1) / step, rowIdx => {
                    int y = rowIdx * step;
                    var localCov = new double[origBands, origBands];
                    for (int x = 0; x < Samples; x += step)
                    {
                        if (!mask[y, x] || float.IsNaN(_cube[0, y, x])) continue;
                        for (int i = 0; i < origBands; i++)
                        {
                            double devI = _cube[i, y, x] - mean[i];
                            for (int j = i; j < origBands; j++)
                                localCov[i, j] += devI * (_cube[j, y, x] - mean[j]);
                        }
                    }
                    lock (syncObj)
                    {
                        for (int i = 0; i < origBands; i++)
                            for (int j = i; j < origBands; j++) cov[i, j] += localCov[i, j];
                    }
                });

                for (int i = 0; i < origBands; i++)
                    for (int j = i; j < origBands; j++)
                    {
                        cov[i, j] /= (n - 1);
                        cov[j, i] = cov[i, j];
                    }

                var evecs = JacobiEigenLocal(cov, origBands);

                // Estabilizar el signo de los autovectores para que coincida visualmente siempre
                for (int pc = 0; pc < numPca; pc++)
                {
                    double maxAbs = 0;
                    int sign = 1;
                    for (int b = 0; b < origBands; b++)
                    {
                        if (Math.Abs(evecs[b, pc]) > maxAbs)
                        {
                            maxAbs = Math.Abs(evecs[b, pc]);
                            sign = Math.Sign(evecs[b, pc]);
                        }
                    }
                    if (sign < 0)
                    {
                        for (int b = 0; b < origBands; b++) evecs[b, pc] = -evecs[b, pc];
                    }
                }

                // 3. Proyectar PCAs y calcular Min/Max de cada componente
                float[] pcMins = new float[numPca];
                float[] pcMaxs = new float[numPca];
                for (int i = 0; i < numPca; i++) { pcMins[i] = float.MaxValue; pcMaxs[i] = float.MinValue; }

                Parallel.For(0, Lines, y => {
                    for (int x = 0; x < Samples; x++)
                    {
                        if (!mask[y, x] || float.IsNaN(_cube[0, y, x]))
                        {
                            for (int pc = 0; pc < numPca; pc++) newCube[origBands + 4 + pc, y, x] = float.NaN;
                            continue;
                        }
                        for (int pc = 0; pc < numPca; pc++)
                        {
                            float val = 0;
                            for (int b = 0; b < origBands; b++)
                            {
                                double dev = _cube[b, y, x] - mean[b];
                                val += (float)(dev * evecs[b, pc]);
                            }
                            newCube[origBands + 4 + pc, y, x] = val;

                            lock (syncObj)
                            {
                                if (val < pcMins[pc]) pcMins[pc] = val;
                                if (val > pcMaxs[pc]) pcMaxs[pc] = val;
                            }
                        }
                    }
                });

                // 4. Normalizar las PCA al rango [0, 1] exactamente como hacía Hyper.cs (rangoPunto/rangoTotal)
                Parallel.For(0, numPca, pc => {
                    float range = pcMaxs[pc] - pcMins[pc];
                    if (range < 1e-9f) range = 1f;
                    for (int y = 0; y < Lines; y++)
                    {
                        for (int x = 0; x < Samples; x++)
                        {
                            float val = newCube[origBands + 4 + pc, y, x];
                            if (!float.IsNaN(val))
                            {
                                newCube[origBands + 4 + pc, y, x] = (val - pcMins[pc]) / range;
                            }
                        }
                    }
                });
            }

            var resultCube = new HyperspectralCube(Header, newCube);
            resultCube.IsCalibrated = this.IsCalibrated;
            resultCube.IsAbsorbance = this.IsAbsorbance;
            return resultCube;
        }

        private double[,] JacobiEigenLocal(double[,] cov, int n)
        {
            double[,] v = new double[n, n];
            for (int i = 0; i < n; i++) v[i, i] = 1.0;

            int maxIter = 100;
            for (int iter = 0; iter < maxIter; iter++)
            {
                double max = 0.0; int p = 0, q = 1;
                for (int i = 0; i < n - 1; i++)
                    for (int j = i + 1; j < n; j++)
                        if (Math.Abs(cov[i, j]) > max) { max = Math.Abs(cov[i, j]); p = i; q = j; }

                if (max < 1e-9) break;

                double theta = (cov[q, q] - cov[p, p]) / (2.0 * cov[p, q]);
                double t = 1.0 / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1.0));
                if (theta < 0) t = -t;

                double c = 1.0 / Math.Sqrt(t * t + 1.0);
                double s = t * c;

                cov[p, p] -= t * cov[p, q];
                cov[q, q] += t * cov[p, q];
                cov[p, q] = 0.0;

                for (int i = 0; i < n; i++)
                {
                    if (i != p && i != q)
                    {
                        double a = cov[p, i], b = cov[q, i];
                        cov[p, i] = cov[i, p] = c * a - s * b;
                        cov[q, i] = cov[i, q] = s * a + c * b;
                    }
                    double vip = v[i, p], viq = v[i, q];
                    v[i, p] = c * vip - s * viq;
                    v[i, q] = s * vip + c * viq;
                }
            }

            var eigenPairs = new List<(double val, double[] vec)>();
            for (int i = 0; i < n; i++)
            {
                double[] vec = new double[n];
                for (int j = 0; j < n; j++) vec[j] = v[j, i];
                eigenPairs.Add((cov[i, i], vec));
            }
            eigenPairs = eigenPairs.OrderByDescending(x => x.val).ToList();

            double[,] result = new double[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++) result[j, i] = eigenPairs[i].vec[j];

            return result;
        }

        public static HyperspectralCube Load(string hdrOrRawPath, IProgress<int>? progress = null)
        {
            string basePath = hdrOrRawPath.EndsWith(".hdr", StringComparison.OrdinalIgnoreCase)
                              ? hdrOrRawPath[..^4] : hdrOrRawPath.EndsWith(".raw", StringComparison.OrdinalIgnoreCase)
                              ? hdrOrRawPath[..^4] : hdrOrRawPath;

            string hdrPath = basePath + ".hdr";
            if (!File.Exists(hdrPath) && hdrOrRawPath.EndsWith(".hdr", StringComparison.OrdinalIgnoreCase)) hdrPath = hdrOrRawPath;

            string rawPath = basePath + ".raw";
            if (!File.Exists(rawPath))
            {
                if (File.Exists(basePath)) rawPath = basePath;
                else foreach (var ext in new[] { ".img", ".dat", ".bil", ".bip", ".bsq" })
                    if (File.Exists(basePath + ext)) { rawPath = basePath + ext; break; }
            }

            if (!File.Exists(rawPath)) throw new FileNotFoundException($"Archivo de datos no encontrado para: {basePath}");

            var header = EnviHeader.Load(hdrPath);
            var cube = ReadRaw(rawPath, header, progress);
            return new HyperspectralCube(header, cube);
        }

        private static float[,,] ReadRaw(string rawPath, EnviHeader h, IProgress<int>? progress)
        {
            var cube = new float[h.Bands, h.Lines, h.Samples];
            using var fs = new FileStream(rawPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
            using var reader = new BinaryReader(fs);

            if (h.HeaderOffset > 0) fs.Seek(h.HeaderOffset, SeekOrigin.Begin);

            switch (h.Interleave)
            {
                case EnviInterleave.BSQ:
                    for (int b = 0; b < h.Bands; b++)
                    {
                        for (int l = 0; l < h.Lines; l++) for (int s = 0; s < h.Samples; s++) cube[b, l, s] = ReadValue(reader, h);
                        progress?.Report((b + 1) * 100 / h.Bands);
                    }
                    break;
                case EnviInterleave.BIL:
                    for (int l = 0; l < h.Lines; l++)
                    {
                        for (int b = 0; b < h.Bands; b++) for (int s = 0; s < h.Samples; s++) cube[b, l, s] = ReadValue(reader, h);
                        progress?.Report((l + 1) * 100 / h.Lines);
                    }
                    break;
                case EnviInterleave.BIP:
                    for (int l = 0; l < h.Lines; l++)
                    {
                        for (int s = 0; s < h.Samples; s++) for (int b = 0; b < h.Bands; b++) cube[b, l, s] = ReadValue(reader, h);
                        progress?.Report((l + 1) * 100 / h.Lines);
                    }
                    break;
            }
            return cube;
        }

        private static float ReadValue(BinaryReader r, EnviHeader h)
        {
            return h.DataType switch
            {
                EnviDataType.Byte => r.ReadByte(),
                EnviDataType.Int16 => h.IsBigEndian ? BitConverter.ToInt16(Reverse(r.ReadBytes(2))) : r.ReadInt16(),
                EnviDataType.UInt16 => h.IsBigEndian ? BitConverter.ToUInt16(Reverse(r.ReadBytes(2))) : r.ReadUInt16(),
                EnviDataType.Int32 => h.IsBigEndian ? BitConverter.ToInt32(Reverse(r.ReadBytes(4))) : r.ReadInt32(),
                EnviDataType.UInt32 => h.IsBigEndian ? BitConverter.ToUInt32(Reverse(r.ReadBytes(4))) : r.ReadUInt32(),
                EnviDataType.Float32 => h.IsBigEndian ? BitConverter.ToSingle(Reverse(r.ReadBytes(4))) : r.ReadSingle(),
                EnviDataType.Float64 => (float)(h.IsBigEndian ? BitConverter.ToDouble(Reverse(r.ReadBytes(8))) : r.ReadDouble()),
                _ => r.ReadSingle()
            };
        }
        private static byte[] Reverse(byte[] b) { Array.Reverse(b); return b; }

        private void ComputeStats()
        {
            float min = float.MaxValue, max = float.MinValue;
            for (int b = 0; b < Bands; b++)
                for (int l = 0; l < Lines; l++)
                    for (int s = 0; s < Samples; s++)
                    {
                        float v = _cube[b, l, s];
                        if (float.IsNaN(v)) continue;
                        if (v < min) min = v;
                        if (v > max) max = v;
                    }
            GlobalMin = min == float.MaxValue ? 0f : min;
            GlobalMax = max == float.MinValue ? 1f : max;
        }
    }
}