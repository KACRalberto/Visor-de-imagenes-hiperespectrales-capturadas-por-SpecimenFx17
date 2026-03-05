using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SpecimenFX17.Imaging
{
    /// <summary>
    /// Tipos de interleave del formato ENVI
    /// BIL = Band Interleaved by Line  → [rows][bands][cols]
    /// BIP = Band Interleaved by Pixel → [rows][cols][bands]
    /// BSQ = Band Sequential           → [bands][rows][cols]
    /// </summary>
    public enum EnviInterleave { BIL, BIP, BSQ }

    /// <summary>
    /// Tipos de dato del formato ENVI
    /// </summary>
    public enum EnviDataType
    {
        Byte = 1,
        Int16 = 2,
        Int32 = 3,
        Float32 = 4,
        Float64 = 5,
        UInt16 = 12,
        UInt32 = 13
    }

    /// <summary>
    /// Parsea el archivo de cabecera ENVI (.hdr) que acompaña al archivo .raw
    /// </summary>
    public class EnviHeader
    {
        // ── Dimensiones ──────────────────────────────────────────────────────
        public int Samples  { get; private set; }   // Columnas (ancho)
        public int Lines    { get; private set; }   // Filas (alto)
        public int Bands    { get; private set; }   // Número de bandas espectrales

        // ── Formato ──────────────────────────────────────────────────────────
        public EnviDataType DataType   { get; private set; } = EnviDataType.Float32;
        public EnviInterleave Interleave { get; private set; } = EnviInterleave.BIL;
        public int HeaderOffset { get; private set; } = 0;
        public string ByteOrder  { get; private set; } = "0";   // 0=little-endian, 1=big-endian

        // ── Espectro ─────────────────────────────────────────────────────────
        public List<double> Wavelengths    { get; private set; } = new();
        public string WavelengthUnits      { get; private set; } = "nm";

        // ── Metadatos extra ──────────────────────────────────────────────────
        public string Description         { get; private set; } = string.Empty;
        public List<string> BandNames     { get; private set; } = new();
        public double DataIgnoreValue     { get; private set; } = double.NaN;
        public Dictionary<string, string> RawFields { get; } = new(StringComparer.OrdinalIgnoreCase);

        // ── Derivados ────────────────────────────────────────────────────────
        /// <summary>Bytes que ocupa un valor según DataType</summary>
        public int BytesPerValue => DataType switch
        {
            EnviDataType.Byte    => 1,
            EnviDataType.Int16   => 2,
            EnviDataType.UInt16  => 2,
            EnviDataType.Int32   => 4,
            EnviDataType.UInt32  => 4,
            EnviDataType.Float32 => 4,
            EnviDataType.Float64 => 8,
            _ => 4
        };

        public bool IsBigEndian => ByteOrder == "1";

        // ─────────────────────────────────────────────────────────────────────
        //  PARSING
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Carga y parsea un archivo .hdr ENVI.
        /// </summary>
        public static EnviHeader Load(string hdrPath)
        {
            if (!File.Exists(hdrPath))
                throw new FileNotFoundException($"Archivo .hdr no encontrado: {hdrPath}");

            var h = new EnviHeader();
            string text = File.ReadAllText(hdrPath);

            // Eliminar comentarios (líneas que empiezan con ';')
            var lines = text.Split('\n')
                            .Select(l => l.Trim())
                            .Where(l => !l.StartsWith(';'))
                            .ToArray();

            // Re-unir para manejar valores multi-línea con llaves { }
            string clean = string.Join("\n", lines);

            // Separar en pares clave = valor
            var dict = ParseKeyValues(clean);

            foreach (var kv in dict)
                h.RawFields[kv.Key] = kv.Value;

            // ── Rellenar propiedades ──────────────────────────────────────────
            if (dict.TryGetValue("samples",  out var s))  h.Samples  = int.Parse(s.Trim());
            if (dict.TryGetValue("lines",    out var l))  h.Lines    = int.Parse(l.Trim());
            if (dict.TryGetValue("bands",    out var b))  h.Bands    = int.Parse(b.Trim());
            if (dict.TryGetValue("header offset", out var ho)) h.HeaderOffset = int.Parse(ho.Trim());
            if (dict.TryGetValue("byte order",    out var bo)) h.ByteOrder    = bo.Trim();
            if (dict.TryGetValue("description",   out var desc)) h.Description = desc.Trim();
            if (dict.TryGetValue("wavelength units", out var wu)) h.WavelengthUnits = wu.Trim();

            if (dict.TryGetValue("data type", out var dt))
                h.DataType = (EnviDataType)int.Parse(dt.Trim());

            if (dict.TryGetValue("interleave", out var il))
                h.Interleave = il.Trim().ToUpperInvariant() switch
                {
                    "BIL" => EnviInterleave.BIL,
                    "BIP" => EnviInterleave.BIP,
                    "BSQ" => EnviInterleave.BSQ,
                    _ => EnviInterleave.BIL
                };

            if (dict.TryGetValue("data ignore value", out var div) &&
                double.TryParse(div.Trim(), out double divVal))
                h.DataIgnoreValue = divVal;

            // ── Longitudes de onda ────────────────────────────────────────────
            if (dict.TryGetValue("wavelength", out var wl))
                h.Wavelengths = ParseNumericList(wl);

            // ── Nombres de banda ─────────────────────────────────────────────
            if (dict.TryGetValue("band names", out var bn))
                h.BandNames = ParseStringList(bn);

            // Generar wavelengths si no están pero hay band names con nm
            if (h.Wavelengths.Count == 0 && h.BandNames.Count > 0)
                h.Wavelengths = TryExtractWavelengthsFromNames(h.BandNames);

            // Si aún faltan, generar rango sintético
            if (h.Wavelengths.Count == 0 && h.Bands > 0)
            {
                Console.WriteLine("[EnviHeader] Wavelengths no encontrados → generando rango 400-900 nm");
                double step = (h.Bands > 1) ? 500.0 / (h.Bands - 1) : 0;
                for (int i = 0; i < h.Bands; i++)
                    h.Wavelengths.Add(400.0 + i * step);
            }

            return h;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Helpers de parseo
        // ─────────────────────────────────────────────────────────────────────

        private static Dictionary<string, string> ParseKeyValues(string text)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            int i = 0;
            // Saltar línea "ENVI" inicial si existe
            if (text.TrimStart().StartsWith("ENVI", StringComparison.OrdinalIgnoreCase))
                i = text.IndexOf('\n') + 1;

            while (i < text.Length)
            {
                int eq = text.IndexOf('=', i);
                if (eq < 0) break;

                string key = text[i..eq].Trim();

                int valStart = eq + 1;
                // ¿Comienza con '{'? → leer hasta '}'
                int vsi = valStart;
                while (vsi < text.Length && text[vsi] == ' ') vsi++;

                string value;
                if (vsi < text.Length && text[vsi] == '{')
                {
                    int close = text.IndexOf('}', vsi);
                    if (close < 0) close = text.Length - 1;
                    value = text[(vsi + 1)..close].Trim();
                    i = close + 1;
                    // Avanzar hasta siguiente línea
                    int nl = text.IndexOf('\n', i);
                    i = nl < 0 ? text.Length : nl + 1;
                }
                else
                {
                    int nl = text.IndexOf('\n', valStart);
                    int end = nl < 0 ? text.Length : nl;
                    value = text[valStart..end].Trim();
                    i = end + 1;
                }

                if (!string.IsNullOrWhiteSpace(key))
                    dict[key] = value;
            }

            return dict;
        }

        private static List<double> ParseNumericList(string raw)
        {
            var result = new List<double>();
            foreach (var token in raw.Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                if (double.TryParse(token.Trim(), System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out double v))
                    result.Add(v);
            return result;
        }

        private static List<string> ParseStringList(string raw) =>
            raw.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();

        private static List<double> TryExtractWavelengthsFromNames(List<string> names)
        {
            var result = new List<double>();
            foreach (var name in names)
            {
                var digits = new string(name.Where(c => char.IsDigit(c) || c == '.').ToArray());
                if (double.TryParse(digits, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out double v))
                    result.Add(v);
                else
                    return new List<double>(); // fallback global
            }
            return result;
        }

        public override string ToString() =>
            $"ENVI Header | {Samples}×{Lines} px | {Bands} bandas | {Interleave} | {DataType} | λ: {Wavelengths.FirstOrDefault():F1}–{Wavelengths.LastOrDefault():F1} {WavelengthUnits}";
    }
}
