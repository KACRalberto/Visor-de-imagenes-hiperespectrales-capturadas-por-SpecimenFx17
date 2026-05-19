using System.Collections.Generic;

namespace SpecimenFX17.Imaging
{
    public class SegmentationParams
    {
        public int Threshold { get; set; } = 154;
        public bool InvertThreshold { get; set; } = true;

        public int OpenIters { get; set; } = 1;
        public int CloseIters { get; set; } = 2;
        public int MinArea { get; set; } = 4000;

        // --- NUEVO: CEGUERA PERIMETRAL (Ignorar etiquetas) ---
        public int IgnoreTopPct { get; set; } = 0;    // Porcentaje superior a ignorar
        public int IgnoreBottomPct { get; set; } = 0; // Porcentaje inferior a ignorar

        // --- NUEVO: CEGUERA PERIMETRAL HORIZONTAL ---
        public int IgnoreLeftPct { get; set; } = 0;   // Porcentaje izquierdo a ignorar
        public int IgnoreRightPct { get; set; } = 0;  // Porcentaje derecho a ignorar
        public List<System.Drawing.Point> PointsToRepair { get; set; } = new();
        public List<System.Drawing.Point> PointsToRemove { get; set; } = new();

        public float StretchMin { get; set; } = float.NaN;
        public float StretchMax { get; set; } = float.NaN;
    }
}