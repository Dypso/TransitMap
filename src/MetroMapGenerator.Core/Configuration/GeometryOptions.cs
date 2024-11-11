namespace MetroMapGenerator.Core.Configuration
{
    public class GeometryOptions
    {
        public double InitialTemperature { get; set; } = 1.0;
        public double CoolingFactor { get; set; } = 0.95;
        public double StopCriterion { get; set; } = 0.01;
        public double BendPenalty { get; set; } = 1.0;
        public double OverlapPenalty { get; set; } = 2.0;
    }
}