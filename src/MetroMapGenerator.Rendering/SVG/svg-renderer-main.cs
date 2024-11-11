using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using MetroMapGenerator.Core.Interfaces;
using MetroMapGenerator.Core.Models;
using MetroMapGenerator.Core.Configuration;
using MetroMapGenerator.Processing.Graph;
using MetroMapGenerator.Rendering.Models;

namespace MetroMapGenerator.Rendering.SVG
{
    public partial class SVGRenderer : ISVGRenderer
    {
        private readonly StringBuilder _builder;
        private Vector2 _minBounds;
        private Vector2 _maxBounds;
        private Vector2 _scale;
        private Vector2 _offset;
        private readonly Dictionary<string, MetroLine> _metroLines;
        private readonly Dictionary<string, string> _lineColors;
        private readonly Dictionary<int, StationInfo> _stationInfo;

        // Constantes de style et mise en page
        private const float DEFAULT_GRID_SIZE = 50f;           // Taille de base de la grille
        private const float STROKE_WIDTH_MAIN = 8f;           // Épaisseur des lignes principales
        private const float STROKE_WIDTH_SECONDARY = 6f;      // Épaisseur des lignes secondaires
        private const float STATION_RADIUS_REGULAR = 4f;      // Rayon des stations normales
        private const float STATION_RADIUS_TRANSFER = 7f;     // Rayon des stations de correspondance
        private const float LABEL_OFFSET = 14f;              // Décalage des labels
        private const float MIN_LABEL_DISTANCE = 30f;        // Distance minimale entre labels
        private const int CURVE_INTERPOLATION_STEPS = 10;    // Pas d'interpolation pour les courbes
        private const float PARALLEL_LINE_OFFSET = 12f;      // Décalage pour lignes parallèles
        private const string FONT_FAMILY = "Helvetica, Arial, sans-serif";

        public SVGRenderer()
        {
            _builder = new StringBuilder();
            _metroLines = new Dictionary<string, MetroLine>();
            _lineColors = InitializeLineColors();
            _stationInfo = new Dictionary<int, StationInfo>();
            _minBounds = new Vector2(float.MaxValue, float.MaxValue);
            _maxBounds = new Vector2(float.MinValue, float.MinValue);
            _scale = Vector2.One;
            _offset = Vector2.Zero;
        }

        public async Task<string> RenderMapAsync(
            IEnumerable<TransportNode> nodes,
            IEnumerable<TransportEdge> edges,
            RenderingOptions options)
        {
            try
            {
                var nodesList = nodes?.ToList() ?? throw new ArgumentNullException(nameof(nodes));
                var edgesList = edges?.ToList() ?? throw new ArgumentNullException(nameof(edges));

                Console.WriteLine($"Starting SVG rendering with {nodesList.Count} nodes and {edgesList.Count} edges");

                // 1. Préparation des données
                await PrepareRenderingData(nodesList, edgesList, options);

                // 2. Génération du SVG
                _builder.Clear();
                await GenerateSVG(options);

                return _builder.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in SVG rendering: {ex.Message}");
                throw;
            }
        }

        private async Task PrepareRenderingData(
            List<TransportNode> nodes,
            List<TransportEdge> edges,
            RenderingOptions options)
        {
            await Task.Run(() =>
            {
                // Calculer les limites et l'échelle
                CalculateBoundsAndScale(nodes, options);

                // Analyser les stations et leurs connexions
                AnalyzeStations(nodes, edges);

                // Extraire et préparer les lignes
                ExtractMetroLines(nodes, edges);

                // Optimiser le placement des labels
                OptimizeLabelPlacements();
            });
        }

        private void CalculateBoundsAndScale(List<TransportNode> nodes, RenderingOptions options)
        {
            _minBounds = new Vector2(
                nodes.Min(n => n.Position.X),
                nodes.Min(n => n.Position.Y)
            );
            _maxBounds = new Vector2(
                nodes.Max(n => n.Position.X),
                nodes.Max(n => n.Position.Y)
            );

            var width = _maxBounds.X - _minBounds.X;
            var height = _maxBounds.Y - _minBounds.Y;

            // Calculer l'échelle pour maintenir les proportions
            var scaleX = (float)(options.Width - 2 * options.Padding) / width;
            var scaleY = (float)(options.Height - 2 * options.Padding) / height;
            var scale = Math.Min(scaleX, scaleY);

            _scale = new Vector2(scale, -scale); // Y inversé pour SVG
            _offset = new Vector2(
                (float)(options.Padding + (options.Width - 2 * options.Padding - width * scale) / 2),
                (float)(options.Height - options.Padding - (options.Height - 2 * options.Padding - height * scale) / 2)
            );
        }

        private void AnalyzeStations(List<TransportNode> nodes, List<TransportEdge> edges)
        {
            _stationInfo.Clear();
            foreach (var node in nodes)
            {
                var info = new StationInfo
                {
                    Node = node,
                    ConnectedLines = new HashSet<string>(),
                    ConnectedStations = new HashSet<int>()
                };

                // Trouver toutes les lignes passant par cette station
                var connectedEdges = edges.Where(e => 
                    e.SourceNodeId == node.Id || e.TargetNodeId == node.Id);
                
                foreach (var edge in connectedEdges)
                {
                    info.ConnectedLines.Add(GetMainLineId(edge.RouteId));
                    info.ConnectedStations.Add(edge.SourceNodeId == node.Id ? 
                        edge.TargetNodeId : edge.SourceNodeId);
                }

                // Déterminer l'importance de la station
                info.Importance = CalculateStationImportance(info);
                info.Position = TransformPoint(node.Position);
                
                _stationInfo[node.Id] = info;
            }
        }

        private void ExtractMetroLines(List<TransportNode> nodes, List<TransportEdge> edges)
        {
            _metroLines.Clear();
            var routeGroups = edges.GroupBy(e => GetMainLineId(e.RouteId));

            foreach (var group in routeGroups)
            {
                var stations = new HashSet<TransportNode>();
                var segments = new List<LineSegment>();

                foreach (var edge in group)
                {
                    var sourceNode = nodes.First(n => n.Id == edge.SourceNodeId);
                    var targetNode = nodes.First(n => n.Id == edge.TargetNodeId);

                    stations.Add(sourceNode);
                    stations.Add(targetNode);

                    segments.Add(new LineSegment
                    {
                        EdgeId = edge.Id,
                        SourceId = sourceNode.Id,
                        TargetId = targetNode.Id,
                        RouteId = edge.RouteId,
                        Start = TransformPoint(sourceNode.Position),
                        End = TransformPoint(targetNode.Position),
                        IsTransfer = sourceNode.Type == NodeType.Transfer || targetNode.Type == NodeType.Transfer
                    });
                }

                var line = new MetroLine
                {
                    Id = group.Key,
                    Color = GetLineColor(group.Key),
                    Name = GetLineName(group.Key),
                    Segments = segments,
                    Stations = stations.ToList()
                };

                line.Importance = CalculateLineImportance(line);
                _metroLines[group.Key] = line;
            }
        }

        private void OptimizeLabelPlacements()
        {
            var processedStations = new HashSet<int>();

            foreach (var line in _metroLines.Values.OrderByDescending(l => l.Importance))
            {
                foreach (var station in line.Stations.OrderByDescending(s => _stationInfo[s.Id].Importance))
                {
                    if (processedStations.Contains(station.Id)) continue;

                    var info = _stationInfo[station.Id];
                    info.LabelPosition = CalculateOptimalLabelPosition(station, line);
                    processedStations.Add(station.Id);
                }
            }
        }

        private async Task GenerateSVG(RenderingOptions options)
        {
            // En-tête SVG
            WriteHeader(options);

            // Définitions (gradients, filters, etc.)
            WriteDefinitions(options);

            // Styles
            WriteStyles(options);

            // Lignes de métro (ordre d'importance)
            foreach (var line in _metroLines.Values.OrderBy(l => l.Importance))
            {
                await WriteMetroLine(line, options);
            }

            // Stations (par dessus les lignes)
            foreach (var station in _stationInfo.Values.OrderBy(s => s.Importance))
            {
                await WriteStation(station, options);
            }

            // Labels (en dernier pour être au-dessus)
            foreach (var station in _stationInfo.Values.OrderBy(s => s.Importance))
            {
                await WriteStationLabel(station, options);
            }

            // Fermeture SVG
            WriteFooter();
        }

        private Dictionary<string, string> InitializeLineColors()
        {
            return new Dictionary<string, string>
            {
                {"A", "#E8308A"}, // Rose
                {"B", "#0075BF"}, // Bleu
                {"C", "#F59C00"}, // Orange
                {"D", "#009E3D"}, // Vert
                {"E", "#8C368C"}, // Violet
                {"F", "#778186"}, // Gris
                {"M", "#0075BF"}, // Bleu (Metro)
                {"T", "#E8308A"}, // Rose (Tram)
                {"C", "#F59C00"}, // Orange (C lines)
                {"S", "#009E3D"}, // Vert (Shuttle)
            };
        }

        private string GetMainLineId(string routeId) => routeId.Split('_')[0];

        private string GetLineName(string lineId)
        {
            if (lineId.Length == 1)
            {
                return $"Line {lineId}";
            }
            return lineId;
        }

        private string GetLineColor(string routeId)
        {
            if (_lineColors.TryGetValue(routeId, out var color))
                return color;

            // Générer une couleur unique pour les lignes non définies
            var hash = routeId.GetHashCode();
            var hue = Math.Abs(hash % 360);
            return $"hsl({hue}, 70%, 45%)";
        }
    }
}