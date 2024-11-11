using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using MetroMapGenerator.Core.Models;
using MetroMapGenerator.Core.Interfaces;
using NetTopologySuite.Geometries;
using System.Numerics;
using NetTopologySuite.Index.Strtree;
using MetroMapGenerator.Core.Configuration;

namespace MetroMapGenerator.Processing.Graph
{
    public class GraphBuilder : IGraphBuilder
    {
        private STRtree<TransportNode> _spatialIndex;
        private readonly Dictionary<int, TransportNode> _nodeCache;
        private readonly ProcessingOptions _options;
        private const double DEFAULT_CLUSTERING_DISTANCE = 0.05;

        public GraphBuilder(ProcessingOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _spatialIndex = new STRtree<TransportNode>();
            _nodeCache = new Dictionary<int, TransportNode>();
        }

        public async Task<(IEnumerable<TransportNode> nodes, IEnumerable<TransportEdge> edges)> BuildTransportGraphAsync(
            IEnumerable<TransportNode> nodes,
            IEnumerable<TransportEdge> edges)
        {
            var nodesList = nodes?.ToList() ?? throw new ArgumentNullException(nameof(nodes));
            var edgesList = edges?.ToList() ?? throw new ArgumentNullException(nameof(edges));

            Console.WriteLine($"BuildTransportGraphAsync received {nodesList.Count} nodes and {edgesList.Count} edges");

            if (!nodesList.Any())
            {
                throw new ArgumentException("No nodes provided for graph building");
            }

            // Optimisation des nœuds
            var optimizedNodes = await Task.Run(() =>
            {
                LogCoordinateRanges("Before optimization", nodesList);
                BuildSpatialIndex(nodesList);
                CacheNodes(nodesList);
                
                var result = OptimizeNodePositions(nodesList, edgesList);
                return result;
            });

            // Optimisation des arêtes
            var optimizedEdges = await Task.Run(() =>
            {
                var result = OptimizeEdges(optimizedNodes, edgesList);
                Console.WriteLine($"Optimized edges count: {result.Count()}");
                return result;
            });

            // Filtrage final des nœuds invalides
            var finalNodes = await Task.Run(() =>
            {
                var validNodes = optimizedNodes.Where(n => 
                    n != null && 
                    !float.IsNaN(n.Position.X) && 
                    !float.IsNaN(n.Position.Y) &&
                    !float.IsInfinity(n.Position.X) && 
                    !float.IsInfinity(n.Position.Y))
                    .ToList();

                Console.WriteLine($"Final valid nodes count: {validNodes.Count}");
                LogCoordinateRanges("After optimization", validNodes);
                return validNodes;
            });

            return (finalNodes, optimizedEdges);
        }

        private void LogCoordinateRanges(string stage, IEnumerable<TransportNode> nodes)
        {
            if (!nodes.Any()) return;

            var validNodes = nodes.Where(n => 
                !float.IsNaN(n.Position.X) && 
                !float.IsNaN(n.Position.Y) &&
                !float.IsInfinity(n.Position.X) && 
                !float.IsInfinity(n.Position.Y)).ToList();

            if (!validNodes.Any())
            {
                Console.WriteLine($"{stage}: No valid nodes found for coordinate range calculation");
                return;
            }

            var minX = validNodes.Min(n => n.Position.X);
            var maxX = validNodes.Max(n => n.Position.X);
            var minY = validNodes.Min(n => n.Position.Y);
            var maxY = validNodes.Max(n => n.Position.Y);

            Console.WriteLine($"{stage} coordinate ranges:");
            Console.WriteLine($"X: {minX:F6} to {maxX:F6}");
            Console.WriteLine($"Y: {minY:F6} to {maxY:F6}");
            Console.WriteLine($"Valid nodes: {validNodes.Count} out of {nodes.Count()}");
        }

        private void BuildSpatialIndex(List<TransportNode> nodes)
        {
            _spatialIndex = new STRtree<TransportNode>();
            
            foreach (var node in nodes.Where(IsValidNode))
            {
                var envelope = CreateEnvelopeForNode(node);
                _spatialIndex.Insert(envelope, node);
            }
        }

        private void CacheNodes(IEnumerable<TransportNode> nodes)
        {
            _nodeCache.Clear();
            foreach (var node in nodes.Where(IsValidNode))
            {
                _nodeCache[node.Id] = node;
            }
        }

        private bool IsValidNode(TransportNode node)
        {
            return node != null &&
                   !float.IsNaN(node.Position.X) && 
                   !float.IsNaN(node.Position.Y) &&
                   !float.IsInfinity(node.Position.X) && 
                   !float.IsInfinity(node.Position.Y);
        }

        private List<TransportNode> OptimizeNodePositions(List<TransportNode> nodes, List<TransportEdge> edges)
        {
            var optimizedNodes = new List<TransportNode>();
            var processedNodes = new HashSet<int>();
            var routeGroups = edges.GroupBy(e => e.RouteId).ToDictionary(g => g.Key, g => g.ToList());

            Console.WriteLine($"OptimizeNodePositions processing {nodes.Count} nodes in {routeGroups.Count} routes");

            // Traiter chaque route séparément
            foreach (var routeGroup in routeGroups)
            {
                var routeEdges = routeGroup.Value;
                var routeNodes = new HashSet<int>();
                
                // Collecter tous les nœuds de cette route
                foreach (var edge in routeEdges)
                {
                    routeNodes.Add(edge.SourceNodeId);
                    routeNodes.Add(edge.TargetNodeId);
                }

                // Traiter les nœuds de la route
                foreach (var nodeId in routeNodes)
                {
                    if (processedNodes.Contains(nodeId))
                        continue;

                    var node = nodes.FirstOrDefault(n => n.Id == nodeId);
                    if (node == null || !IsValidNode(node))
                        continue;

                    // Ne chercher les nœuds proches que dans la même route
                    var nearbyNodes = FindNearbyNodesInRoute(node, routeNodes, _options.NodeClusteringDistance)
                        .Where(n => !processedNodes.Contains(n.Id))
                        .ToList();

                    if (nearbyNodes.Any())
                    {
                        var cluster = new List<TransportNode> { node };
                        cluster.AddRange(nearbyNodes);
                        var mergedNode = MergeNodes(cluster);
                        
                        if (IsValidNode(mergedNode))
                        {
                            optimizedNodes.Add(mergedNode);
                            processedNodes.Add(node.Id);
                            processedNodes.UnionWith(nearbyNodes.Select(n => n.Id));
                        }
                    }
                    else if (!processedNodes.Contains(node.Id))
                    {
                        optimizedNodes.Add(node);
                        processedNodes.Add(node.Id);
                    }
                }
            }

            // Traiter les nœuds isolés qui ne font partie d'aucune route
            foreach (var node in nodes)
            {
                if (!processedNodes.Contains(node.Id) && IsValidNode(node))
                {
                    optimizedNodes.Add(node);
                    processedNodes.Add(node.Id);
                }
            }

            Console.WriteLine($"OptimizeNodePositions produced {optimizedNodes.Count} optimized nodes");
            return optimizedNodes;
        }

        private IEnumerable<TransportNode> FindNearbyNodesInRoute(
            TransportNode node,
            HashSet<int> routeNodes,
            double radius)
        {
            if (radius <= 0) radius = DEFAULT_CLUSTERING_DISTANCE;
            
            var searchEnvelope = CreateSearchEnvelope(node.Position, radius);
            var nearbyNodes = _spatialIndex.Query(searchEnvelope)
                .Where(n => n.Id != node.Id && routeNodes.Contains(n.Id))
                .Where(n => IsValidNode(n))
                .Where(n => Vector2.Distance(n.Position, node.Position) <= radius)
                .ToList();

            return nearbyNodes;
        }

        private Envelope CreateSearchEnvelope(Vector2 position, double radius)
        {
            return new Envelope(
                position.X - radius,
                position.X + radius,
                position.Y - radius,
                position.Y + radius
            );
        }

        private Envelope CreateEnvelopeForNode(TransportNode node)
        {
            var radius = _options.NodeClusteringDistance > 0 ? 
                _options.NodeClusteringDistance : DEFAULT_CLUSTERING_DISTANCE;
                
            return new Envelope(
                node.Position.X - radius,
                node.Position.X + radius,
                node.Position.Y - radius,
                node.Position.Y + radius
            );
        }

        private TransportNode MergeNodes(List<TransportNode> nodes)
        {
            if (!nodes.Any() || nodes.All(n => !IsValidNode(n)))
                throw new ArgumentException("No valid nodes to merge");

            var validNodes = nodes.Where(IsValidNode).ToList();
            
            // Calculer la position moyenne
            var centerPos = new Vector2(
                validNodes.Average(n => n.Position.X),
                validNodes.Average(n => n.Position.Y)
            );

            // Utiliser le premier nœud comme base
            var baseNode = validNodes[0];
            return new TransportNode(
                baseNode.Id,
                baseNode.StopId,
                baseNode.Name,
                centerPos,
                validNodes.Count > 1 ? NodeType.Transfer : baseNode.Type
            );
        }

        private IEnumerable<TransportEdge> OptimizeEdges(
            IEnumerable<TransportNode> nodes,
            List<TransportEdge> edges)
        {
            var nodeDict = nodes.ToDictionary(n => n.Id);
            var optimizedEdges = new HashSet<TransportEdge>();

            foreach (var edge in edges)
            {
                if (!nodeDict.TryGetValue(edge.SourceNodeId, out var source) || 
                    !nodeDict.TryGetValue(edge.TargetNodeId, out var target))
                    continue;

                // Vérification supplémentaire
                if (source == null || target == null)
                    continue;

                if (!IsValidNode(source) || !IsValidNode(target))
                    continue;

                var distance = Vector2.Distance(source.Position, target.Position);
                var weight = CalculateEdgeWeight(distance);

                var optimizedEdge = new TransportEdge(
                    edge.Id,
                    edge.SourceNodeId,
                    edge.TargetNodeId,
                    edge.RouteId,
                    weight
                );

                optimizedEdges.Add(optimizedEdge);
            }

            return optimizedEdges;
        }

        private double CalculateEdgeWeight(float distance)
        {
            const float minWeight = 0.1f;
            const float maxWeight = 1.0f;
            const float normalizer = 0.1f;
            
            return Math.Max(minWeight, Math.Min(maxWeight, distance / normalizer));
        }
    }
}