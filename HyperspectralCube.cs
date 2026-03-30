using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing;

namespace SpecimenFX17.Imaging
{
    public class BandStat
    {
        public float Min;
        public float Max;
        public float Mean;
        public float Std;
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  MOTOR DE ALMACENAMIENTO: MEMORY-MAPPED FILES
    // ──────────────────────────────────────────────────────────────────────────
    internal unsafe interface ICubeStorage : IDisposable
    {
        int Bands { get; }
        int Lines { get; }
        int Samples { get; }
        float Get(int b, int l, int s);
        void Set(int b, int l, int s, float v);
        ICubeStorage Clone();
        TempMappedStorage ConvertToWritable(CancellationToken ct = default);
    }

    internal unsafe class TempMappedStorage : ICubeStorage
    {
        private MemoryMappedFile? _mmf;
        private MemoryMappedViewAccessor? _acc;
        private byte* _ptr = null;
        public int Bands { get; }
        public int Lines { get; }
        public int Samples { get; }

        public TempMappedStorage(int bands, int lines, int samples)
        {
            Bands = bands; Lines = lines; Samples = samples;
            long bytes = (long)Bands * Lines * Samples * 4L;

            _mmf = MemoryMappedFile.CreateNew(null, bytes, MemoryMappedFileAccess.ReadWrite);
            _acc = _mmf.CreateViewAccessor(0, bytes, MemoryMappedFileAccess.ReadWrite);
            _acc.SafeMemoryMappedViewHandle.AcquirePointer(ref _ptr);
        }

        public float Get(int b, int l, int s) => ((float*)_ptr)[((long)b * Lines + l) * Samples + s];
        public void Set(int b, int l, int s, float v) => ((float*)_ptr)[((long)b * Lines + l) * Samples + s] = v;

        public ICubeStorage Clone()
        {
            var clone = new TempMappedStorage(Bands, Lines, Samples);
            long bytes = (long)Bands * Lines * Samples * 4L;
            Buffer.MemoryCopy(_ptr, clone._ptr, bytes, bytes);
            return clone;
        }

        public TempMappedStorage ConvertToWritable(CancellationToken ct = default) => this;

        public void Dispose()
        {
            if (_ptr != null) { _acc?.SafeMemoryMappedViewHandle.ReleasePointer(); _ptr = null; }
            _acc?.Dispose(); _mmf?.Dispose();
        }
    }

    internal unsafe class MappedFileStorage : ICubeStorage
    {
        private FileStream? _fs;
        private MemoryMappedFile? _mmf;
        private MemoryMappedViewAccessor? _acc;
        private byte* _ptr = null;
        private EnviHeader _h;

        public int Bands => _h.Bands;
        public int Lines => _h.Lines;
        public int Samples => _h.Samples;

        public MappedFileStorage(string path, EnviHeader h)
        {
            _h = h;
            _fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            long capacity = _fs.Length;
            _mmf = MemoryMappedFile.CreateFromFile(_fs, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, false);
            _acc = _mmf.CreateViewAccessor(0, capacity, MemoryMappedFileAccess.Read);
            _acc.SafeMemoryMappedViewHandle.AcquirePointer(ref _ptr);
        }

        public float Get(int b, int l, int s)
        {
            long off = _h.HeaderOffset;
            long B = _h.Bands, L = _h.Lines, S = _h.Samples;
            long bVal = _h.BytesPerValue;

            long pos = _h.Interleave switch
            {
                EnviInterleave.BSQ => off + ((long)b * L * S + l * S + s) * bVal,
                EnviInterleave.BIL => off + ((long)l * B * S + b * S + s) * bVal,
                EnviInterleave.BIP => off + ((long)l * S * B + s * B + b) * bVal,
                _ => 0
            };

            return ReadValue(_ptr + pos);
        }

        private float ReadValue(byte* p)
        {
            if (!_h.IsBigEndian)
            {
                return _h.DataType switch
                {
                    EnviDataType.Byte => *p,
                    EnviDataType.Int16 => *(short*)p,
                    EnviDataType.UInt16 => *(ushort*)p,
                    EnviDataType.Int32 => *(int*)p,
                    EnviDataType.UInt32 => *(uint*)p,
                    EnviDataType.Float32 => *(float*)p,
                    EnviDataType.Float64 => (float)*(double*)p,
                    _ => *(float*)p
                };
            }
            else
            {
                return _h.DataType switch
                {
                    EnviDataType.Byte => *p,
                    EnviDataType.Int16 => (short)((p[0] << 8) | p[1]),
                    EnviDataType.UInt16 => (ushort)((p[0] << 8) | p[1]),
                    EnviDataType.Int32 => (p[0] << 24) | (p[1] << 16) | (p[2] << 8) | p[3],
                    EnviDataType.UInt32 => (uint)((p[0] << 24) | (p[1] << 16) | (p[2] << 8) | p[3]),
                    EnviDataType.Float32 => ReverseFloat32(p),
                    EnviDataType.Float64 => ReverseFloat64(p),
                    _ => 0f
                };
            }
        }

        private float ReverseFloat32(byte* p) { int i = (p[0] << 24) | (p[1] << 16) | (p[2] << 8) | p[3]; return *(float*)&i; }
        private float ReverseFloat64(byte* p) { long l = ((long)p[0] << 56) | ((long)p[1] << 48) | ((long)p[2] << 40) | ((long)p[3] << 32) | ((long)p[4] << 24) | ((long)p[5] << 16) | ((long)p[6] << 8) | p[7]; return (float)*(double*)&l; }

        public void Set(int b, int l, int s, float v) => throw new InvalidOperationException("No se puede escribir en el archivo RAW original.");

        public TempMappedStorage ConvertToWritable(CancellationToken ct = default)
        {
            var temp = new TempMappedStorage(Bands, Lines, Samples);
            var po = new ParallelOptions { CancellationToken = ct };
            try
            {
                Parallel.For(0, Bands, po, b => {
                    for (int l = 0; l < Lines; l++)
                        for (int s = 0; s < Samples; s++)
                            temp.Set(b, l, s, Get(b, l, s));
                });
                return temp;
            }
            catch
            {
                temp.Dispose();
                throw;
            }
        }

        public ICubeStorage Clone() => ConvertToWritable();

        public void Dispose()
        {
            if (_ptr != null) { _acc?.SafeMemoryMappedViewHandle.ReleasePointer(); _ptr = null; }
            _acc?.Dispose(); _mmf?.Dispose(); _fs?.Dispose();
        }
    }


    // ──────────────────────────────────────────────────────────────────────────
    // CLASE PRINCIPAL
    // ──────────────────────────────────────────────────────────────────────────
    public class HyperspectralCube : IDisposable
    {
        public EnviHeader Header { get; }
        private ICubeStorage _storage;

        public int Bands => _storage.Bands;
        public int Lines => _storage.Lines;
        public int Samples => _storage.Samples;

        public float GlobalMin { get; private set; }
        public float GlobalMax { get; private set; }
        public bool IsCalibrated { get; private set; } = false;
        public bool IsAbsorbance { get; private set; } = false;

        public List<BandStat> BandStats { get; private set; } = new();
        public string AnalysisReport { get; set; } = "";
        public Guid Version { get; private set; } = Guid.NewGuid();

        internal HyperspectralCube(EnviHeader header, ICubeStorage storage)
        {
            Header = header;
            _storage = storage;
            ComputeStats();
        }

        ~HyperspectralCube() => Dispose(false);

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            _storage?.Dispose();
        }

        public HyperspectralCube Clone()
        {
            var cloneStorage = _storage.Clone();
            return new HyperspectralCube(Header, cloneStorage)
            {
                IsCalibrated = this.IsCalibrated,
                IsAbsorbance = this.IsAbsorbance,
                AnalysisReport = this.AnalysisReport,
                Version = Guid.NewGuid()
            };
        }

        private void EnsureWritable(CancellationToken ct = default)
        {
            if (_storage is MappedFileStorage mfs)
            {
                var temp = mfs.ConvertToWritable(ct);
                _storage.Dispose();
                _storage = temp;
            }
        }

        public float this[int band, int line, int sample]
        {
            get => _storage.Get(band, line, sample);
            set { EnsureWritable(); _storage.Set(band, line, sample, value); }
        }

        public float[,] GetBand(int bandIndex)
        {
            if (bandIndex < 0 || bandIndex >= Bands) throw new ArgumentOutOfRangeException(nameof(bandIndex));
            var img = new float[Lines, Samples];
            for (int l = 0; l < Lines; l++)
                for (int s = 0; s < Samples; s++)
                    img[l, s] = _storage.Get(bandIndex, l, s);
            return img;
        }

        public float[] GetSpectrum(int line, int sample)
        {
            var spec = new float[Bands];
            for (int b = 0; b < Bands; b++) spec[b] = _storage.Get(b, line, sample);
            return spec;
        }

        public (float Min, float Max) GetBandStats(int bandIndex)
        {
            float min = float.MaxValue, max = float.MinValue;
            float ignore = (float)Header.DataIgnoreValue;

            for (int l = 0; l < Lines; l++)
                for (int s = 0; s < Samples; s++)
                {
                    float v = _storage.Get(bandIndex, l, s);
                    if (float.IsNaN(v) || Math.Abs(v - ignore) < 1e-5) continue;
                    if (v < min) min = v;
                    if (v > max) max = v;
                }

            return (min == float.MaxValue ? 0f : min, max == float.MinValue ? 1f : max);
        }

        public void ApplySpatialRotation(float angleDegrees, CancellationToken ct = default)
        {
            if (angleDegrees == 0f || angleDegrees % 360 == 0) return;

            double angleRad = angleDegrees * Math.PI / 180.0;
            double cosA = Math.Cos(angleRad);
            double sinA = Math.Sin(angleRad);

            int oldW = Samples, oldH = Lines;
            double cx = oldW / 2.0, cy = oldH / 2.0;

            PointF[] corners = { new PointF(0, 0), new PointF(oldW, 0), new PointF(0, oldH), new PointF(oldW, oldH) };
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;

            foreach (var pt in corners)
            {
                double rx = cosA * (pt.X - cx) - sinA * (pt.Y - cy) + cx;
                double ry = sinA * (pt.X - cx) + cosA * (pt.Y - cy) + cy;
                if (rx < minX) minX = rx; if (rx > maxX) maxX = rx;
                if (ry < minY) minY = ry; if (ry > maxY) maxY = ry;
            }

            int newW = (int)Math.Ceiling(maxX - minX);
            int newH = (int)Math.Ceiling(maxY - minY);

            var newStorage = new TempMappedStorage(Bands, newH, newW);
            var po = new ParallelOptions { CancellationToken = ct };

            try
            {
                Parallel.For(0, Bands, po, b => {
                    for (int y = 0; y < newH; y++)
                    {
                        for (int x = 0; x < newW; x++)
                        {
                            double origX = cosA * ((x + minX) - cx) + sinA * ((y + minY) - cy) + cx;
                            double origY = -sinA * ((x + minX) - cx) + cosA * ((y + minY) - cy) + cy;

                            int ix = (int)Math.Round(origX), iy = (int)Math.Round(origY);

                            if (ix >= 0 && ix < oldW && iy >= 0 && iy < oldH)
                                newStorage.Set(b, y, x, _storage.Get(b, iy, ix));
                            else
                                newStorage.Set(b, y, x, float.NaN);
                        }
                    }
                });

                _storage.Dispose();
                _storage = newStorage;
                ComputeStats();
                Version = Guid.NewGuid();
            }
            catch
            {
                newStorage.Dispose();
                throw;
            }
        }

        public void Calibrate(HyperspectralCube whiteRef, HyperspectralCube darkRef, CancellationToken ct = default)
        {
            if (whiteRef.Samples != Samples || darkRef.Samples != Samples)
                throw new ArgumentException($"El ancho de la imagen ({Samples}px) no coincide con las referencias.");

            EnsureWritable(ct);
            var po = new ParallelOptions { CancellationToken = ct };

            int[] wMap = new int[Bands], dMap = new int[Bands];
            for (int b = 0; b < Bands; b++)
            {
                double targetWl = (Header.Wavelengths != null && Header.Wavelengths.Count > b) ? Header.Wavelengths[b] : 0;
                wMap[b] = GetClosestBandIndex(whiteRef, targetWl, b);
                dMap[b] = GetClosestBandIndex(darkRef, targetWl, b);
            }

            float[,] maxWhite = new float[Bands, Samples], minDark = new float[Bands, Samples];

            Parallel.For(0, Bands, po, b =>
            {
                int mappedW = wMap[b], mappedD = dMap[b];
                for (int s = 0; s < Samples; s++)
                {
                    float wMax = float.MinValue, dMin = float.MaxValue;
                    for (int l = 0; l < whiteRef.Lines; l++) { float v = whiteRef[mappedW, l, s]; if (!float.IsNaN(v) && !float.IsInfinity(v) && v > wMax) wMax = v; }
                    for (int l = 0; l < darkRef.Lines; l++) { float v = darkRef[mappedD, l, s]; if (!float.IsNaN(v) && !float.IsInfinity(v) && v < dMin) dMin = v; }
                    maxWhite[b, s] = wMax == float.MinValue ? 1f : wMax;
                    minDark[b, s] = dMin == float.MaxValue ? 0f : dMin;
                }
            });

            Parallel.For(0, Bands, po, b =>
            {
                for (int l = 0; l < Lines; l++)
                {
                    for (int s = 0; s < Samples; s++)
                    {
                        float val = _storage.Get(b, l, s);
                        if (float.IsNaN(val) || float.IsInfinity(val)) continue;

                        float range = maxWhite[b, s] - minDark[b, s];
                        if (range <= 0.0001f) range = 0.0001f;

                        float res = ((val - minDark[b, s]) / range) * 0.99f;
                        if (float.IsNaN(res) || float.IsInfinity(res) || res < 0f) res = 0f;
                        else if (res > 1.0f) res = 1.0f;

                        _storage.Set(b, l, s, res);
                    }
                }
            });

            IsCalibrated = true; IsAbsorbance = false;
            ComputeStats(); Version = Guid.NewGuid();
        }

        private int GetClosestBandIndex(HyperspectralCube refCube, double targetWl, int fallbackIndex)
        {
            if (refCube.Header.Wavelengths == null || refCube.Header.Wavelengths.Count == 0 || targetWl == 0)
                return Math.Clamp((int)Math.Round((double)fallbackIndex / Math.Max(1, Bands - 1) * (refCube.Bands - 1)), 0, refCube.Bands - 1);

            int bestIdx = 0; double minDiff = double.MaxValue;
            for (int i = 0; i < refCube.Header.Wavelengths.Count; i++)
            {
                double diff = Math.Abs(refCube.Header.Wavelengths[i] - targetWl);
                if (diff < minDiff) { minDiff = diff; bestIdx = i; }
            }
            return bestIdx;
        }

        public void ConvertToAbsorbance(CancellationToken ct = default)
        {
            if (!IsCalibrated || IsAbsorbance) return;
            EnsureWritable(ct);
            var po = new ParallelOptions { CancellationToken = ct };

            Parallel.For(0, Bands, po, b =>
            {
                for (int l = 0; l < Lines; l++)
                    for (int s = 0; s < Samples; s++)
                    {
                        float r = _storage.Get(b, l, s);
                        if (!float.IsNaN(r) && !float.IsInfinity(r))
                        {
                            float res = r <= 0.0001f ? (float)-Math.Log10(0.0001) : (float)-Math.Log10(r);
                            if (float.IsNaN(res) || float.IsInfinity(res)) res = 0f;
                            _storage.Set(b, l, s, res);
                        }
                    }
            });
            IsAbsorbance = true; ComputeStats(); Version = Guid.NewGuid();
        }

        public void ApplySNV(CancellationToken ct = default)
        {
            EnsureWritable(ct);
            var po = new ParallelOptions { CancellationToken = ct };

            Parallel.For(0, Lines, po, l => {
                for (int s = 0; s < Samples; s++)
                {
                    float mean = 0; int valid = 0;
                    for (int b = 0; b < Bands; b++) { float v = _storage.Get(b, l, s); if (!float.IsNaN(v) && !float.IsInfinity(v)) { mean += v; valid++; } }
                    if (valid == 0) continue;
                    mean /= valid;

                    float variance = 0;
                    for (int b = 0; b < Bands; b++) { float v = _storage.Get(b, l, s); if (!float.IsNaN(v) && !float.IsInfinity(v)) variance += (v - mean) * (v - mean); }
                    float std = (float)Math.Sqrt(variance / valid);
                    if (std < 1e-6f || float.IsNaN(std)) std = 1f;

                    for (int b = 0; b < Bands; b++)
                    {
                        float v = _storage.Get(b, l, s);
                        if (!float.IsNaN(v) && !float.IsInfinity(v))
                        {
                            float res = (v - mean) / std;
                            if (float.IsNaN(res) || float.IsInfinity(res)) res = 0f;
                            _storage.Set(b, l, s, res);
                        }
                    }
                }
            });
            ComputeStats(); Version = Guid.NewGuid();
        }

        public void ApplyMSC(CancellationToken ct = default)
        {
            EnsureWritable(ct);
            var po = new ParallelOptions { CancellationToken = ct };

            double[] meanSpec = new double[Bands]; int[] validCounts = new int[Bands];
            for (int l = 0; l < Lines; l++) { for (int s = 0; s < Samples; s++) { for (int b = 0; b < Bands; b++) { float v = _storage.Get(b, l, s); if (!float.IsNaN(v) && !float.IsInfinity(v)) { meanSpec[b] += v; validCounts[b]++; } } } }
            for (int b = 0; b < Bands; b++) if (validCounts[b] > 0) meanSpec[b] /= validCounts[b];

            Parallel.For(0, Lines, po, l => {
                for (int s = 0; s < Samples; s++)
                {
                    double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0; int n = 0;
                    for (int b = 0; b < Bands; b++) { float y = _storage.Get(b, l, s); if (!float.IsNaN(y) && !float.IsInfinity(y)) { double x = meanSpec[b]; sumX += x; sumY += y; sumXY += x * y; sumX2 += x * x; n++; } }
                    if (n < 2) continue;

                    double xMean = sumX / n, yMean = sumY / n, denominator = sumX2 - n * xMean * xMean;
                    if (Math.Abs(denominator) < 1e-9) continue;

                    double m = (sumXY - n * xMean * yMean) / denominator, a = yMean - m * xMean;
                    if (Math.Abs(m) < 1e-9 || double.IsNaN(m)) m = 1;

                    for (int b = 0; b < Bands; b++)
                    {
                        float v = _storage.Get(b, l, s);
                        if (!float.IsNaN(v) && !float.IsInfinity(v))
                        {
                            float res = (float)((v - a) / m);
                            if (float.IsNaN(res) || float.IsInfinity(res)) res = 0f;
                            _storage.Set(b, l, s, res);
                        }
                    }
                }
            });
            ComputeStats(); Version = Guid.NewGuid();
        }

        public void ApplySavitzkyGolay(int windowSize, int polyOrder, int derivOrder, CancellationToken ct = default)
        {
            if (Bands < 4) return;
            if (windowSize % 2 == 0) windowSize++;
            if (windowSize > Bands) windowSize = (Bands % 2 == 0) ? Bands - 1 : Bands;
            if (windowSize < 3 || polyOrder >= windowSize) return;

            double[] coeffs = GetSavitzkyGolayCoefficients(windowSize, polyOrder, derivOrder);
            int m = windowSize / 2;

            var newStorage = new TempMappedStorage(Bands, Lines, Samples);
            var po = new ParallelOptions { CancellationToken = ct };

            try
            {
                Parallel.For(0, Lines, po, l => {
                    for (int s = 0; s < Samples; s++)
                    {
                        float[] spec = new float[Bands];
                        for (int b = 0; b < Bands; b++) spec[b] = _storage.Get(b, l, s);

                        for (int b = 0; b < Bands; b++)
                        {
                            if (float.IsNaN(spec[b]) || float.IsInfinity(spec[b])) { newStorage.Set(b, l, s, float.NaN); continue; }
                            double sum = 0;
                            for (int i = -m; i <= m; i++) sum += spec[Math.Clamp(b + i, 0, Bands - 1)] * coeffs[i + m];

                            float result = (float)sum;
                            if (float.IsNaN(result) || float.IsInfinity(result)) result = 0f;
                            newStorage.Set(b, l, s, result);
                        }
                    }
                });

                _storage.Dispose();
                _storage = newStorage;
                ComputeStats(); Version = Guid.NewGuid();
            }
            catch
            {
                newStorage.Dispose();
                throw;
            }
        }

        private static double[] GetSavitzkyGolayCoefficients(int windowSize, int polyOrder, int derivOrder)
        {
            int m = windowSize / 2, rows = windowSize, cols = polyOrder + 1;
            double[,] J = new double[rows, cols], Jt = new double[cols, rows], JtJ = new double[cols, cols];
            for (int i = 0; i < rows; i++) for (int j = 0; j < cols; j++) J[i, j] = Math.Pow(i - m, j);
            for (int i = 0; i < rows; i++) for (int j = 0; j < cols; j++) Jt[j, i] = J[i, j];
            for (int i = 0; i < cols; i++) for (int j = 0; j < cols; j++) for (int k = 0; k < rows; k++) JtJ[i, j] += Jt[i, k] * J[k, j];

            double[,] InvJtJ = InvertMatrix(JtJ, cols);
            double[,] C = new double[cols, rows];
            for (int i = 0; i < cols; i++) for (int j = 0; j < rows; j++) for (int k = 0; k < cols; k++) C[i, j] += InvJtJ[i, k] * Jt[k, j];

            double[] coeffs = new double[rows]; double fact = 1;
            for (int i = 1; i <= derivOrder; i++) fact *= i;
            for (int i = 0; i < rows; i++) coeffs[i] = C[derivOrder, i] * fact;
            return coeffs;
        }

        private static double[,] InvertMatrix(double[,] matrix, int n)
        {
            double[,] result = new double[n, n], aug = new double[n, 2 * n];
            for (int i = 0; i < n; i++) { for (int j = 0; j < n; j++) aug[i, j] = matrix[i, j]; aug[i, n + i] = 1.0; }
            for (int i = 0; i < n; i++)
            {
                double pivot = aug[i, i]; if (Math.Abs(pivot) < 1e-9) throw new Exception("Matriz singular en Savitzky-Golay.");
                for (int j = 0; j < 2 * n; j++) aug[i, j] /= pivot;
                for (int k = 0; k < n; k++) if (k != i) { double factor = aug[k, i]; for (int j = 0; j < 2 * n; j++) aug[k, j] -= factor * aug[i, j]; }
            }
            for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) result[i, j] = aug[i, n + j];
            return result;
        }

        public void ApplySpatialMedianFilter(int kernelSize, CancellationToken ct = default)
        {
            if (kernelSize <= 1) return;
            int offset = kernelSize / 2;
            var newStorage = new TempMappedStorage(Bands, Lines, Samples);
            var po = new ParallelOptions { CancellationToken = ct };

            try
            {
                Parallel.For(0, Bands, po, b =>
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
                                    window[count++] = _storage.Get(b, yy, xx);
                                }
                            }
                            Array.Sort(window, 0, count);
                            newStorage.Set(b, l, s, window[count / 2]);
                        }
                    }
                });
                _storage.Dispose();
                _storage = newStorage;
                ComputeStats(); Version = Guid.NewGuid();
            }
            catch
            {
                newStorage.Dispose();
                throw;
            }
        }

        public HyperspectralCube GenerateAnalyzedCube(int numPca = 10, bool[,]? mask = null, CancellationToken ct = default)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int origBands = Header.Bands;

            int newBands = origBands + 4 + numPca;
            var newStorage = new TempMappedStorage(newBands, Lines, Samples);
            var po = new ParallelOptions { CancellationToken = ct };

            try
            {
                if (mask == null)
                {
                    mask = new bool[Lines, Samples];
                    for (int l = 0; l < Lines; l++) for (int s = 0; s < Samples; s++) mask[l, s] = true;
                }

                Parallel.For(0, Lines, po, y => {
                    for (int x = 0; x < Samples; x++)
                    {
                        float min = float.MaxValue, max = float.MinValue, sum = 0;
                        int valid = 0;

                        for (int b = 0; b < origBands; b++)
                        {
                            float v = _storage.Get(b, y, x);
                            newStorage.Set(b, y, x, v);
                            if (!float.IsNaN(v) && !float.IsInfinity(v))
                            {
                                if (v < min) min = v;
                                if (v > max) max = v;
                                sum += v;
                                valid++;
                            }
                        }

                        if (!mask[y, x] || float.IsNaN(_storage.Get(0, y, x)))
                        {
                            newStorage.Set(origBands, y, x, float.NaN);
                            newStorage.Set(origBands + 1, y, x, float.NaN);
                            newStorage.Set(origBands + 2, y, x, float.NaN);
                            newStorage.Set(origBands + 3, y, x, float.NaN);
                        }
                        else if (valid > 0)
                        {
                            newStorage.Set(origBands, y, x, sum / valid);
                            newStorage.Set(origBands + 1, y, x, min);
                            newStorage.Set(origBands + 2, y, x, max);
                            newStorage.Set(origBands + 3, y, x, max - min);
                        }
                    }
                });

                var mean = new double[origBands];
                int n = 0;
                object syncObj = new object();

                Parallel.For(0, Lines, po, y => {
                    double[] localMean = new double[origBands];
                    int localN = 0;
                    for (int x = 0; x < Samples; x++)
                    {
                        if (!mask[y, x] || float.IsNaN(_storage.Get(0, y, x))) continue;
                        for (int b = 0; b < origBands; b++)
                        {
                            float v = _storage.Get(b, y, x);
                            if (!float.IsNaN(v) && !float.IsInfinity(v)) localMean[b] += v;
                        }
                        localN++;
                    }
                    lock (syncObj) { for (int b = 0; b < origBands; b++) mean[b] += localMean[b]; n += localN; }
                });

                string report = $"Bandas para PCA: {origBands}\n";

                if (n > 1)
                {
                    for (int b = 0; b < origBands; b++) mean[b] /= n;

                    var cov = new double[origBands, origBands];
                    Parallel.For(0, Lines, po, y => {
                        var localCov = new double[origBands, origBands];
                        for (int x = 0; x < Samples; x++)
                        {
                            if (!mask[y, x] || float.IsNaN(_storage.Get(0, y, x))) continue;
                            for (int i = 0; i < origBands; i++)
                            {
                                float vi = _storage.Get(i, y, x);
                                if (float.IsNaN(vi) || float.IsInfinity(vi)) continue;
                                double devI = vi - mean[i];

                                for (int j = i; j < origBands; j++)
                                {
                                    float vj = _storage.Get(j, y, x);
                                    if (!float.IsNaN(vj) && !float.IsInfinity(vj))
                                        localCov[i, j] += devI * (vj - mean[j]);
                                }
                            }
                        }
                        lock (syncObj)
                        {
                            for (int i = 0; i < origBands; i++)
                                for (int j = i; j < origBands; j++) cov[i, j] += localCov[i, j];
                        }
                    });

                    for (int i = 0; i < origBands; i++)
                        for (int j = i; j < origBands; j++) { cov[i, j] /= (n - 1); cov[j, i] = cov[i, j]; }

                    var evecs = JacobiEigenLocal(cov, origBands);

                    float[] pcMins = new float[numPca], pcMaxs = new float[numPca];
                    for (int i = 0; i < numPca; i++) { pcMins[i] = float.MaxValue; pcMaxs[i] = float.MinValue; }

                    Parallel.For(0, Lines, po, y => {
                        for (int x = 0; x < Samples; x++)
                        {
                            if (!mask[y, x] || float.IsNaN(_storage.Get(0, y, x)))
                            {
                                for (int pc = 0; pc < numPca; pc++) newStorage.Set(origBands + 4 + pc, y, x, float.NaN);
                                continue;
                            }
                            for (int pc = 0; pc < numPca; pc++)
                            {
                                float val = 0;
                                for (int b = 0; b < origBands; b++)
                                {
                                    float vb = _storage.Get(b, y, x);
                                    if (!float.IsNaN(vb) && !float.IsInfinity(vb)) val += (float)(vb * evecs[b, pc]);
                                }
                                newStorage.Set(origBands + 4 + pc, y, x, val);
                                lock (syncObj) { if (val < pcMins[pc]) pcMins[pc] = val; if (val > pcMaxs[pc]) pcMaxs[pc] = val; }
                            }
                        }
                    });

                    sw.Stop(); report += $"PCA: {sw.Elapsed.TotalSeconds:F2}s\nRango PC:\n";

                    float globalRan = 0;
                    for (int pc = 0; pc < numPca; pc++)
                    {
                        float r = pcMaxs[pc] - pcMins[pc]; if (r > globalRan) globalRan = r;
                        report += $"PC{pc + 1}: [{pcMins[pc]:G5}, {pcMaxs[pc]:G5}], {r:G5}\n";
                    }

                    Parallel.For(0, numPca, po, pc => {
                        float range = pcMaxs[pc] - pcMins[pc]; if (range < 1e-9f) range = 1f;
                        for (int y = 0; y < Lines; y++)
                        {
                            for (int x = 0; x < Samples; x++)
                            {
                                float val = newStorage.Get(origBands + 4 + pc, y, x);
                                if (!float.IsNaN(val) && !float.IsInfinity(val))
                                {
                                    float res = (val - pcMins[pc]) / range;
                                    if (float.IsNaN(res) || float.IsInfinity(res)) res = 0f;
                                    newStorage.Set(origBands + 4 + pc, y, x, res);
                                }
                            }
                        }
                    });
                }

                return new HyperspectralCube(Header, newStorage)
                {
                    IsCalibrated = this.IsCalibrated,
                    IsAbsorbance = this.IsAbsorbance,
                    AnalysisReport = report,
                    Version = Guid.NewGuid()
                };
            }
            catch
            {
                newStorage.Dispose();
                throw;
            }
        }

        private double[,] JacobiEigenLocal(double[,] cov, int n)
        {
            double[,] v = new double[n, n];
            for (int i = 0; i < n; i++) v[i, i] = 1.0;
            for (int iter = 0; iter < 100; iter++)
            {
                double max = 0.0; int p = 0, q = 1;
                for (int i = 0; i < n - 1; i++) for (int j = i + 1; j < n; j++) if (Math.Abs(cov[i, j]) > max) { max = Math.Abs(cov[i, j]); p = i; q = j; }
                if (max < 1e-9) break;

                double theta = (cov[q, q] - cov[p, p]) / (2.0 * cov[p, q]);
                double t = 1.0 / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1.0));
                if (theta < 0) t = -t;
                double c = 1.0 / Math.Sqrt(t * t + 1.0);
                double s = t * c;

                cov[p, p] -= t * cov[p, q]; cov[q, q] += t * cov[p, q]; cov[p, q] = 0.0;
                for (int i = 0; i < n; i++)
                {
                    if (i != p && i != q) { double a = cov[p, i], b = cov[q, i]; cov[p, i] = cov[i, p] = c * a - s * b; cov[q, i] = cov[i, q] = s * a + c * b; }
                    double vip = v[i, p], viq = v[i, q]; v[i, p] = c * vip - s * viq; v[i, q] = s * vip + c * viq;
                }
            }

            var pairs = new List<(double val, double[] vec)>();
            for (int i = 0; i < n; i++) { double[] vec = new double[n]; for (int j = 0; j < n; j++) vec[j] = v[j, i]; pairs.Add((cov[i, i], vec)); }
            pairs = pairs.OrderByDescending(x => x.val).ToList();
            double[,] result = new double[n, n];
            for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) result[j, i] = pairs[i].vec[j];
            return result;
        }

        // ──────────────────────────────────────────────────────────────────────────
        // LECTURA DIRECTA A MMF
        // ──────────────────────────────────────────────────────────────────────────
        public static HyperspectralCube Load(string hdrOrRawPath, IProgress<int>? progress = null)
        {
            string dir = Path.GetDirectoryName(hdrOrRawPath) ?? "";
            string baseName = Path.GetFileNameWithoutExtension(hdrOrRawPath);

            string hdrPath = Path.Combine(dir, baseName + ".hdr");
            if (!File.Exists(hdrPath) && hdrOrRawPath.EndsWith(".hdr", StringComparison.OrdinalIgnoreCase))
                hdrPath = hdrOrRawPath;

            string rawPath = Path.Combine(dir, baseName + ".raw");
            if (!File.Exists(rawPath))
            {
                string[] exts = { ".img", ".dat", ".bil", ".bip", ".bsq" };
                foreach (var ext in exts) { if (File.Exists(Path.Combine(dir, baseName + ext))) { rawPath = Path.Combine(dir, baseName + ext); break; } }
                if (!File.Exists(rawPath) && File.Exists(Path.Combine(dir, baseName))) rawPath = Path.Combine(dir, baseName);
            }

            if (!File.Exists(rawPath)) throw new FileNotFoundException($"Archivo binario no encontrado: {baseName}");

            var header = EnviHeader.Load(hdrPath);
            var storage = new MappedFileStorage(rawPath, header);
            progress?.Report(100);

            return new HyperspectralCube(header, storage);
        }

        public float[] GetGlobalMeanSpectrum(CancellationToken ct = default)
        {
            float[] meanSpectrum = new float[Bands];
            int[] validCounts = new int[Bands];
            object syncObj = new object();
            var po = new ParallelOptions { CancellationToken = ct };

            Parallel.For(0, Lines, po, l =>
            {
                float[] localSum = new float[Bands];
                int[] localCounts = new int[Bands];

                for (int s = 0; s < Samples; s++)
                {
                    for (int b = 0; b < Bands; b++)
                    {
                        float val = _storage.Get(b, l, s);
                        if (!float.IsNaN(val) && !float.IsInfinity(val))
                        {
                            localSum[b] += val;
                            localCounts[b]++;
                        }
                    }
                }

                lock (syncObj)
                {
                    for (int b = 0; b < Bands; b++)
                    {
                        meanSpectrum[b] += localSum[b];
                        validCounts[b] += localCounts[b];
                    }
                }
            });

            for (int b = 0; b < Bands; b++) meanSpectrum[b] = validCounts[b] > 0 ? meanSpectrum[b] / validCounts[b] : 0f;
            return meanSpectrum;
        }

        private void ComputeStats()
        {
            float min = float.MaxValue, max = float.MinValue;
            BandStats.Clear();
            for (int b = 0; b < Bands; b++)
            {
                float bMin = float.MaxValue, bMax = float.MinValue, sum = 0, sumSq = 0;
                int valid = 0;
                for (int l = 0; l < Lines; l++)
                    for (int s = 0; s < Samples; s++)
                    {
                        float v = _storage.Get(b, l, s);
                        if (float.IsNaN(v) || float.IsInfinity(v)) continue;
                        if (v < bMin) bMin = v; if (v > bMax) bMax = v;
                        if (v < min) min = v; if (v > max) max = v;
                        sum += v; sumSq += v * v;
                        valid++;
                    }
                if (valid > 0)
                {
                    float mean = sum / valid;
                    BandStats.Add(new BandStat { Min = bMin, Max = bMax, Mean = mean, Std = (float)Math.Sqrt((sumSq / valid) - (mean * mean)) });
                }
                else BandStats.Add(new BandStat());
            }
            GlobalMin = min == float.MaxValue ? 0f : min;
            GlobalMax = max == float.MinValue ? 1f : max;
        }
    }
}