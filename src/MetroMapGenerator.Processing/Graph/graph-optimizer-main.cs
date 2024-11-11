using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using MetroMapGenerator.Core.Models;
using MetroMapGenerator.Core.Configuration;
using MetroMapGenerator.Core.Interfaces;
using MetroMapGenerator.Rendering.Models;

namespace MetroMapGenerator.Processing.Graph
{
    public partial class GraphOptimizer : IGraphOptimizer
    {
        private readonly ProcessingOptions _options;
        private readonly Dictionary<string, List<TransportNode>> _routeNodes;
        private readonly Dictionary<string, List<LineSegment>> _routeSegments;
        
        // Constantes pour l'optimisation schématique
        private const float GRID_SIZE = 1.0f;                    
        private const float MIN_STATION_DISTANCE = 2.0f;         
        private const float TRANSFER_SPACING = 1.5f;             
        private const int OCTILINEAR_ANGLE = 45;                
        private const float STRAIGHTNESS_WEIGHT = 0.7f;          
        private const float SPACING_WEIGHT = 0.3f;               
        private const float MIN_LINE_LENGTH = 0.1f;             
        private const float MIN_BUNDLE_DISTANCE = 0.5f;         
        private const int MAX_OPTIMIZATION_ITERATIONS = 100;     
        private const float CONVERGENCE_THRESHOLD = 0.01f;      
        
        private Vector2 _minBounds;
        private Vector2 _maxBounds;
        private Vector2 _scale;
        private readonly Dictionary<int, List<string>> _nodeRoutes;      
        private readonly Dictionary<string, RouteMetrics> _routeMetrics;

        public GraphOptimizer(ProcessingOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _routeNodes = new Dictionary<string, List<TransportNode>>();
            _routeSegments = new Dictionary<string, List<LineSegment>>();
            _nodeRoutes = new Dictionary<int, List<string>>();
            _routeMetrics = new Dictionary<string, RouteMetrics>();
            _minBounds = new Vector2(float.MaxValue, float.MaxValue);
            _maxBounds = new Vector2(float.MinValue, float.MinValue);
            _scale = Vector2.One;
        }

        public IEnumerable<TransportNode> OptimizeLayout(
            IEnumerable<TransportNode> nodes,
            IEnumerable<TransportEdge> edges)
        {
            try
            {
                var nodesList = nodes?.ToList() ?? throw new ArgumentNullException(nameof(nodes));
                var edgesList = edges?.ToList() ?? throw new ArgumentNullException(nameof(edges));

                if (!nodesList.Any())
                {
                    throw new ArgumentException("Node list is empty", nameof(nodes));
                }

                if (!edgesList.Any())
                {
                    throw new ArgumentException("Edge list is empty", nameof(edges));
                }

                Console.WriteLine($"Starting metro map layout optimization with {nodesList.Count} nodes");

                // 1. Analyse initiale et préparation
                AnalyzeNetwork(nodesList, edgesList);
                Console.WriteLine("Network analysis completed");

                // 2. Normalisation des coordonnées
                var normalizedNodes = NormalizeCoordinates(nodesList);
                Console.WriteLine("Coordinates normalized");

                // 3. Classification et regroupement des lignes
                var mainLines = IdentifyMainLines(normalizedNodes, edgesList);
                Console.WriteLine($"Identified {mainLines.Count} main transit lines");

                // 4. Optimisation schématique
                var schematicNodes = OptimizeSchematicLayout(normalizedNodes, mainLines);
                Console.WriteLine("Schematic layout optimization completed");

                // 5. Résolution des conflits et ajustements finaux
                var optimizedNodes = FinalizeLayout(schematicNodes);
                Console.WriteLine($"Layout finalized with {optimizedNodes.Count} nodes");

                // 6. Dénormalisation et validation
                var finalNodes = DenormalizeCoordinates(optimizedNodes);
                ValidateLayout(finalNodes);

                return finalNodes;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in layout optimization: {ex.Message}");
                throw;
            }
        }

        private void AnalyzeNetwork(List<TransportNode> nodes, List<TransportEdge> edges)
        {
            BuildRouteCollections(nodes, edges);

            foreach (var edge in edges)
            {
                var routeId = GetMainRouteId(edge.RouteId);
                
                if (!_routeMetrics.ContainsKey(routeId))
                {
                    _routeMetrics[routeId] = new RouteMetrics
                    {
                        RouteId = routeId,
                        StationCount = 0,
                        TransferCount = 0,
                        TotalLength = 0,
                        IsMainLine = false
                    };
                }

                UpdateRouteMetrics(_routeMetrics[routeId], edge, nodes);
            }

            var maxLength = _routeMetrics.Values.Max(r => r.TotalLength);
            var maxStations = _routeMetrics.Values.Max(r => r.StationCount);
            var maxTransfers = _routeMetrics.Values.Max(r => r.TransferCount);

            foreach (var metric in _routeMetrics.Values)
            {
                metric.CalculateImportanceScore(maxLength, maxStations, maxTransfers);
                metric.IsMainLine = metric.ImportanceScore > 0.5f;
            }

            Console.WriteLine($"Network analysis completed. Found {_routeMetrics.Count} routes.");
            Console.WriteLine($"Main lines: {_routeMetrics.Values.Count(r => r.IsMainLine)}");
        }

        private void BuildRouteCollections(List<TransportNode> nodes, List<TransportEdge> edges)
        {
            _routeNodes.Clear();
            _routeSegments.Clear();
            _nodeRoutes.Clear();

            foreach (var edge in edges)
            {
                var routeId = GetMainRouteId(edge.RouteId);

                // Ajouter les nœuds à la collection de route
                if (!_routeNodes.ContainsKey(routeId))
                {
                    _routeNodes[routeId] = new List<TransportNode>();
                }

                var sourceNode = nodes.First(n => n.Id == edge.SourceNodeId);
                var targetNode = nodes.First(n => n.Id == edge.TargetNodeId);

                if (!_routeNodes[routeId].Contains(sourceNode))
                {
                    _routeNodes[routeId].Add(sourceNode);
                }
                if (!_routeNodes[routeId].Contains(targetNode))
                {
                    _routeNodes[routeId].Add(targetNode);
                }

                // Ajouter l'arête à la collection de segments
                if (!_routeSegments.ContainsKey(routeId))
                {
                    _routeSegments[routeId] = new List<LineSegment>();
                }

                _routeSegments[routeId].Add(new LineSegment
                {
                    EdgeId = edge.Id,
                    SourceId = edge.SourceNodeId,
                    TargetId = edge.TargetNodeId,
                    RouteId = edge.RouteId,
                    Start = sourceNode.Position,
                    End = targetNode.Position,
                    IsTransfer = sourceNode.Type == NodeType.Transfer || targetNode.Type == NodeType.Transfer
                });

                // Mettre à jour les associations nœud-route
                if (!_nodeRoutes.ContainsKey(sourceNode.Id))
                {
                    _nodeRoutes[sourceNode.Id] = new List<string>();
                }
                if (!_nodeRoutes.ContainsKey(targetNode.Id))
                {
                    _nodeRoutes[targetNode.Id] = new List<string>();
                }

                if (!_nodeRoutes[sourceNode.Id].Contains(routeId))
                {
                    _nodeRoutes[sourceNode.Id].Add(routeId);
                }
                if (!_nodeRoutes[targetNode.Id].Contains(routeId))
                {
                    _nodeRoutes[targetNode.Id].Add(routeId);
                }
            }
        }

        private void UpdateRouteMetrics(RouteMetrics metrics, TransportEdge edge, List<TransportNode> nodes)
        {
            var sourceNode = nodes.First(n => n.Id == edge.SourceNodeId);
            var targetNode = nodes.First(n => n.Id == edge.TargetNodeId);

            var uniqueStations = new HashSet<int>(
                nodes.Where(n => n.Type != NodeType.Transfer)
                     .Select(n => n.Id)
            );

            var uniqueTransfers = new HashSet<int>(
                nodes.Where(n => n.Type == NodeType.Transfer)
                     .Select(n => n.Id)
            );

            metrics.StationCount = Math.Max(metrics.StationCount, uniqueStations.Count);
            metrics.TransferCount = Math.Max(metrics.TransferCount, uniqueTransfers.Count);
            metrics.TotalLength += Vector2.Distance(sourceNode.Position, targetNode.Position);
        }

        private static string GetMainRouteId(string routeId)
        {
            return routeId.Split('_')[0];
        }

        private static string GetLineName(string lineId)
        {
            return lineId.Length == 1 ? $"Line {lineId}" : lineId;
        }

        private static string GetLineColor(string routeId)
        {
            var colors = new Dictionary<string, string>
            {
                {"A", "#E8308A"}, // Rose
                {"B", "#0075BF"}, // Bleu
                {"C", "#F59C00"}, // Orange
                {"D", "#009E3D"}, // Vert
                {"M", "#8C368C"}, // Violet
                {"T", "#778186"}  // Gris
            };

            return colors.TryGetValue(routeId.FirstOrDefault().ToString(), out var color) ? color : "#666666";
        }

        private void ValidateLayout(List<TransportNode> nodes)
        {
            if (!nodes.Any())
            {
                throw new InvalidOperationException("No nodes in final layout");
            }

            foreach (var node in nodes)
            {
                if (float.IsNaN(node.Position.X) || float.IsNaN(node.Position.Y) ||
                    float.IsInfinity(node.Position.X) || float.IsInfinity(node.Position.Y))
                {
                    throw new InvalidOperationException($"Invalid coordinates for node {node.Id}");
                }
            }

            // Vérification des distances minimales
            for (int i = 0; i < nodes.Count; i++)
            {
                for (int j = i + 1; j < nodes.Count; j++)
                {
                    var distance = Vector2.Distance(nodes[i].Position, nodes[j].Position);
                    if (distance < MIN_STATION_DISTANCE)
                    {
                        Console.WriteLine($"Warning: Nodes {nodes[i].Id} and {nodes[j].Id} are too close: {distance:F2}");
                    }
                }
            }
        }
    }
}