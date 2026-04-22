namespace SpecimenFX17.Imaging
{
    public class SegmentationParams
    {
        public int Threshold { get; set; } = 127;
        public int OpenIters { get; set; } = 2;
        public int CloseIters { get; set; } = 2;
        public int ErodeIters { get; set; } = 0;
        public int DilateIters { get; set; } = 0;
        public int MinArea { get; set; } = 100;
        public int MaxArea { get; set; } = 100000;

        public bool InvertThreshold { get; set; } = false;

        // Watershed Avanzado
        public bool UseAdvancedWatershed { get; set; } = false;
        public float MinDistance { get; set; } = 40f;
        public float AlphaBlend { get; set; } = 0.35f;
    }
}