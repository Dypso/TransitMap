namespace MetroMapGenerator.Core.Configuration
{
    public class ProcessingOptions
    {
        public double MinStopDistance { get; set; } = 0.5;
        public int AngleSnap { get; set; } = 45;
        public double DenseAreaThreshold { get; set; } = 0.1;
        public double NodeClusteringDistance { get; set; } = 0.05;
        public int ParallelProcesses { get; set; } = 4;
        public int ForceDirectedIterations { get; set; } = 100;
    }
}