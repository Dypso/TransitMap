namespace MetroMapGenerator.Core.Configuration
{
    public class RenderingOptions
    {
        public int Width { get; set; } = 1200;
        public int Height { get; set; } = 800;
        public double StationRadius { get; set; } = 4;
        public double LineWidth { get; set; } = 3;
        public double LabelFontSize { get; set; } = 12;
        public string BackgroundColor { get; set; } = "#ffffff";
        public string LineColor { get; set; } = "#000000";
        public string StationColor { get; set; } = "#ffffff";
        public string StationStrokeColor { get; set; } = "#000000";
        public string LabelColor { get; set; } = "#000000";
        public double Padding { get; set; } = 50;
    }
}