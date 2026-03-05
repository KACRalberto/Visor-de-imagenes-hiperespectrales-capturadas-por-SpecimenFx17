using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;

namespace SpecimenFX17.Imaging
{
    /// <summary>
    /// Lee el archivo binario .raw de una imagen hiperespectral ENVI
    /// y lo expone como cubo [banda, fila, columna] de tipo float.
    ///
    /// Formatos soportados:
    ///   BSQ → [Bands][Lines][Samples]
    ///   BIL → [Lines][Bands][Samples]
    ///   BIP → [Lines][Samples][Bands]
    /// </summary>
    public class HyperspectralCube
    {
        // ── Metadatos ─────────────────────────────────────────────────────────
        public EnviHeader Header  { get; }
        public int Bands          => Header.Bands;
        public int Lines          => Header.Lines;
        public int Samples        => Header.Samples;

        // ── Cubo de datos [banda, fila, col] → float ─────────────────────────
        private readonly float[,,] _cube;   // [band, line, sample]

        // ── Estadísticas globales ─────────────────────────────────────────────
        public float GlobalMin { get; private set; }
        public float GlobalMax { get; private set; }

        private HyperspectralCube(EnviHeader header, float[,,] cube)
        {
            Header = header;
            _cube  = cube;
            ComputeStats();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Acceso al cubo
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Valor en [banda, fila, columna]</summary>
        public float this[int band, int line, int sample] => _cube[band, line, sample];

        /// <summary>Extrae la imagen 2D de una banda completa → float[line, sample]</summary>
        public float[,] GetBand(int bandIndex)
        {
            if (bandIndex < 0 || bandIndex >= Bands)
                throw new ArgumentOutOfRangeException(nameof(bandIndex));

            var img = new float[Lines, Samples];
            for (int l = 0; l < Lines; l++)
                for (int s = 0; s < Samples; s++)
                    img[l, s] = _cube[bandIndex, l, s];
            return img;
        }

        /// <summary>Espectro completo de un píxel (fila, col) → float[bands]</summary>
        public float[] GetSpectrum(int line, int sample)
        {
            var spec = new float[Bands];
            for (int b = 0; b < Bands; b++)
                spec[b] = _cube[b, line, sample];
            return spec;
        }

        /// <summary>
        /// Estadísticas min/max para una banda concreta (ignorando NaN y DataIgnoreValue).
        /// </summary>
        public (float Min, float Max) GetBandStats(int bandIndex)
        {
            float min = float.MaxValue, max = float.MinValue;
            float ignore = (float)Header.DataIgnoreValue;

            for (int l = 0; l < Lines; l++)
                for (int s = 0; s < Samples; s++)
                {
                    float v = _cube[bandIndex, l, s];
                    if (float.IsNaN(v) || v == ignore) continue;
                    if (v < min) min = v;
                    if (v > max) max = v;
                }

            return (min == float.MaxValue ? 0f : min,
                    max == float.MinValue ? 1f : max);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  CARGA DESDE ARCHIVO
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Carga el cubo desde un par de archivos .hdr / .raw
        /// </summary>
        /// <param name="hdrOrRawPath">Ruta a cualquiera de los dos archivos (se detecta automáticamente)</param>
        public static HyperspectralCube Load(string hdrOrRawPath,
                                             IProgress<int>? progress = null)
        {
            // Detectar rutas
            string basePath = hdrOrRawPath.EndsWith(".hdr", StringComparison.OrdinalIgnoreCase)
                              ? hdrOrRawPath[..^4]
                              : hdrOrRawPath.EndsWith(".raw", StringComparison.OrdinalIgnoreCase)
                                ? hdrOrRawPath[..^4]
                                : hdrOrRawPath;

            string hdrPath = basePath + ".hdr";
            string rawPath = basePath + ".raw";

            // Intentar variantes comunes si no existe
            if (!File.Exists(rawPath))
            {
                foreach (var ext in new[] { ".img", ".dat", ".bil", ".bip", ".bsq" })
                {
                    if (File.Exists(basePath + ext)) { rawPath = basePath + ext; break; }
                }
            }

            if (!File.Exists(rawPath))
                throw new FileNotFoundException($"Archivo de datos no encontrado para: {basePath}");

            var header = EnviHeader.Load(hdrPath);
            Console.WriteLine($"[HyperspectralCube] {header}");

            var cube = ReadRaw(rawPath, header, progress);
            return new HyperspectralCube(header, cube);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Lectura binaria
        // ─────────────────────────────────────────────────────────────────────

        private static float[,,] ReadRaw(string rawPath, EnviHeader h,
                                          IProgress<int>? progress)
        {
            var cube   = new float[h.Bands, h.Lines, h.Samples];
            int bpv    = h.BytesPerValue;
            long total = (long)h.Bands * h.Lines * h.Samples;
            long bufferSize = Math.Min(total * bpv, 64 * 1024 * 1024); // 64 MB max buffer

            using var fs     = new FileStream(rawPath, FileMode.Open, FileAccess.Read,
                                              FileShare.Read, 1 << 20);
            using var reader = new BinaryReader(fs);

            // Saltar offset de cabecera
            if (h.HeaderOffset > 0)
                fs.Seek(h.HeaderOffset, SeekOrigin.Begin);

            int processed = 0;

            switch (h.Interleave)
            {
                case EnviInterleave.BSQ:
                    // [band][line][sample]
                    for (int b = 0; b < h.Bands; b++)
                    {
                        for (int l = 0; l < h.Lines; l++)
                            for (int s = 0; s < h.Samples; s++)
                                cube[b, l, s] = ReadValue(reader, h);
                        progress?.Report((b + 1) * 100 / h.Bands);
                    }
                    break;

                case EnviInterleave.BIL:
                    // [line][band][sample]
                    for (int l = 0; l < h.Lines; l++)
                    {
                        for (int b = 0; b < h.Bands; b++)
                            for (int s = 0; s < h.Samples; s++)
                                cube[b, l, s] = ReadValue(reader, h);
                        progress?.Report((l + 1) * 100 / h.Lines);
                    }
                    break;

                case EnviInterleave.BIP:
                    // [line][sample][band]
                    for (int l = 0; l < h.Lines; l++)
                    {
                        for (int s = 0; s < h.Samples; s++)
                            for (int b = 0; b < h.Bands; b++)
                                cube[b, l, s] = ReadValue(reader, h);
                        progress?.Report((l + 1) * 100 / h.Lines);
                    }
                    break;
            }

            return cube;
        }

        private static float ReadValue(BinaryReader r, EnviHeader h)
        {
            float v = h.DataType switch
            {
                EnviDataType.Byte    => r.ReadByte(),
                EnviDataType.Int16   => h.IsBigEndian ? ReadInt16BE(r) : r.ReadInt16(),
                EnviDataType.UInt16  => h.IsBigEndian ? ReadUInt16BE(r) : r.ReadUInt16(),
                EnviDataType.Int32   => h.IsBigEndian ? ReadInt32BE(r) : r.ReadInt32(),
                EnviDataType.UInt32  => h.IsBigEndian ? (float)ReadUInt32BE(r) : r.ReadUInt32(),
                EnviDataType.Float32 => h.IsBigEndian ? ReadFloat32BE(r) : r.ReadSingle(),
                EnviDataType.Float64 => (float)(h.IsBigEndian ? ReadFloat64BE(r) : r.ReadDouble()),
                _ => r.ReadSingle()
            };
            return v;
        }

        // ── Big-Endian helpers ────────────────────────────────────────────────
        private static short  ReadInt16BE(BinaryReader r)   { var b = r.ReadBytes(2); Array.Reverse(b); return BitConverter.ToInt16(b); }
        private static ushort ReadUInt16BE(BinaryReader r)  { var b = r.ReadBytes(2); Array.Reverse(b); return BitConverter.ToUInt16(b); }
        private static int    ReadInt32BE(BinaryReader r)   { var b = r.ReadBytes(4); Array.Reverse(b); return BitConverter.ToInt32(b); }
        private static uint   ReadUInt32BE(BinaryReader r)  { var b = r.ReadBytes(4); Array.Reverse(b); return BitConverter.ToUInt32(b); }
        private static float  ReadFloat32BE(BinaryReader r) { var b = r.ReadBytes(4); Array.Reverse(b); return BitConverter.ToSingle(b); }
        private static double ReadFloat64BE(BinaryReader r) { var b = r.ReadBytes(8); Array.Reverse(b); return BitConverter.ToDouble(b); }

        // ─────────────────────────────────────────────────────────────────────
        //  Estadísticas globales
        // ─────────────────────────────────────────────────────────────────────

        private void ComputeStats()
        {
            float min = float.MaxValue, max = float.MinValue;
            float ignore = (float)Header.DataIgnoreValue;

            for (int b = 0; b < Bands; b++)
                for (int l = 0; l < Lines; l++)
                    for (int s = 0; s < Samples; s++)
                    {
                        float v = _cube[b, l, s];
                        if (float.IsNaN(v) || v == ignore) continue;
                        if (v < min) min = v;
                        if (v > max) max = v;
                    }

            GlobalMin = min == float.MaxValue ? 0f : min;
            GlobalMax = max == float.MinValue ? 1f : max;
        }
    }
}
