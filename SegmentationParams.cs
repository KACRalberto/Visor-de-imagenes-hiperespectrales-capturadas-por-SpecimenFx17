using System.Collections.Generic;

namespace SpecimenFX17.Imaging
{
    public class SegmentationParams
    {
        // Parámetros para Umbral Adaptativo (AdaptiveThreshold)
        public int BlockSize { get; set; } = 25; // Tamaño de la vecindad local (debe ser impar)
        public double ConstantC { get; set; } = 10; // Constante restada de la media local

        public bool InvertThreshold { get; set; } = false;
        public int OpenIters { get; set; } = 2;
        public int CloseIters { get; set; } = 2;
        public int MinArea { get; set; } = 100;

        // Puntos seleccionados manualmente para borrar (FloodFill)
        public List<System.Drawing.Point> PointsToRemove { get; set; } = new();
    }
}