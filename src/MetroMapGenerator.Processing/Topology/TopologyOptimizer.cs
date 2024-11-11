using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using MetroMapGenerator.Core.Models;
using MetroMapGenerator.Core.Configuration;

namespace MetroMapGenerator.Processing.Topology
{
    public class TopologyOptimizer
    {
        private readonly ProcessingOptions _options;
        private readonly GeometryOptions _geometryOptions;
        private readonly STRtree<TransportNode> _spatialIndex;
        private readonly Dictionary<int, List<int>> _adjacencyList;
        private const double EPSILON = 1e-10;
        private const double MIN_COORD_DELTA = 1e-6;

        public TopologyOptimizer(ProcessingOptions options, GeometryOptions geometryOptions)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _geometryOptions = geometryOptions ?? throw new ArgumentNullException(nameof(geometryOptions));
            _spatialIndex = new STRtree<TransportNode>();
            _adjacencyList = new Dictionary<int, List<int>>();
        }

        public IEnumerable<TransportNode> OptimizeTopology(
            IEnumerable<TransportNode> nodes,
            IEnumerable<TransportEdge> edges)
        {
            if (nodes == null) throw new ArgumentNullException(nameof(nodes));
            if (edges == null) throw new ArgumentNullException(nameof(edges));


            var nodesList = nodes.ToList();
            Console.WriteLine($"OptimizeTopology received {nodesList.Count} nodes");


            var validNodes = nodes.Where(n => IsValidNode(n)).ToList();

            Console.WriteLine($"Found {validNodes.Count} valid nodes");
    
            // Log les coordonnées min/max pour vérifier la normalisation
            if (validNodes.Any())
            {
                var minX = validNodes.Min(n => n.Position.X);
                var maxX = validNodes.Max(n => n.Position.X);
                var minY = validNodes.Min(n => n.Position.Y);
                var maxY = validNodes.Max(n => n.Position.Y);
                Console.WriteLine($"Coordinate ranges: X({minX:F6} to {maxX:F6}), Y({minY:F6} to {maxY:F6})");
            }
            
            
            if (!validNodes.Any())
            {
                // Log quelques exemples de nœuds invalides pour diagnostic
                foreach (var node in nodesList.Take(5))
                {
                    Console.WriteLine($"Sample node {node.Id}: StopId='{node.StopId}', Name='{node.Name}', " +
                                    $"Pos=({node.Position.X:F6}, {node.Position.Y:F6})");
                }
                throw new InvalidOperationException("No valid nodes provided for topology optimization");
            }

            try
            {
                Console.WriteLine($"Starting topology optimization with {validNodes.Count} nodes");
                
                // Normalize coordinates to prevent numerical issues
                NormalizeCoordinates(validNodes);
                
                // Build spatial index and adjacency list
                BuildSpatialIndex(validNodes);
                BuildAdjacencyList(edges);

                Console.WriteLine("Applying DBSCAN clustering...");
                var clusters = ApplyDBSCAN(validNodes);
                var mergedNodes = MergeClusters(clusters);

                if (!mergedNodes.Any())
                {
                    throw new InvalidOperationException("Node clustering produced no valid results");
                }

                Console.WriteLine($"Merged into {mergedNodes.Count} nodes");

                Console.WriteLine("Applying force-directed layout...");
                ApplyForceDirectedLayout(mergedNodes);

                Console.WriteLine("Aligning angles...");
                AlignAngles(mergedNodes);

                Console.WriteLine("Simplifying segments...");
                SimplifySegments(mergedNodes);

                return mergedNodes;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during topology optimization: {ex.Message}");
                throw;
            }
        }

        private bool IsValidNode(TransportNode? node)
        {
            if (node == null)
            {
                Console.WriteLine($"Node is null");
                return false;
            }
            
            // Vector2 est un type valeur, pas besoin de vérifier null
            if (float.IsNaN(node.Position.X) || float.IsInfinity(node.Position.X))
            {
                Console.WriteLine($"Node {node.Id}: Invalid X coordinate ({node.Position.X})");
                return false;
            }
            
            if (float.IsNaN(node.Position.Y) || float.IsInfinity(node.Position.Y))
            {
                Console.WriteLine($"Node {node.Id}: Invalid Y coordinate ({node.Position.Y})");
                return false;
            }

            if (node.Id < 0)
            {
                Console.WriteLine($"Node {node.Id}: Negative ID");
                return false;
            }

            if (string.IsNullOrWhiteSpace(node.StopId))
            {
                Console.WriteLine($"Node {node.Id}: Empty StopId");
                return false;
            }

            if (string.IsNullOrWhiteSpace(node.Name))
            {
                Console.WriteLine($"Node {node.Id}: Empty Name");
                return false;
            }

            return true;
        }

        private void NormalizeCoordinates(List<TransportNode> nodes)
        {
            if (!nodes.Any()) return;

            var minX = nodes.Min(n => n.Position.X);
            var minY = nodes.Min(n => n.Position.Y);
            var maxX = nodes.Max(n => n.Position.X);
            var maxY = nodes.Max(n => n.Position.Y);

            var width = maxX - minX;
            var height = maxY - minY;

            if (width < MIN_COORD_DELTA || height < MIN_COORD_DELTA)
            {
                throw new InvalidOperationException("Node coordinates are too close together for meaningful normalization");
            }

            var scaleX = 1.0f / width;
            var scaleY = 1.0f / height;

            for (int i = 0; i < nodes.Count; i++)
            {
                var normalizedPos = new Vector2(
                    (nodes[i].Position.X - minX) * scaleX,
                    (nodes[i].Position.Y - minY) * scaleY
                );
                nodes[i] = nodes[i].WithPosition(normalizedPos);
            }
        }

        private void BuildSpatialIndex(List<TransportNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (!IsValidNode(node)) continue;

                var envelope = CreateEnvelopeForNode(node);
                if (envelope != null)
                {
                    _spatialIndex.Insert(envelope, node);
                }
            }
        }

        private Envelope? CreateEnvelopeForNode(TransportNode node)
        {
            try
            {
                var buffer = Math.Max(EPSILON, _options.NodeClusteringDistance / 2);
                return new Envelope(
                    node.Position.X - buffer,
                    node.Position.X + buffer,
                    node.Position.Y - buffer,
                    node.Position.Y + buffer
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating envelope for node {node.Id}: {ex.Message}");
                return null;
            }
        }


        private void BuildAdjacencyList(IEnumerable<TransportEdge> edges)
        {
            _adjacencyList.Clear();
            foreach (var edge in edges)
            {
                if (!_adjacencyList.ContainsKey(edge.SourceNodeId))
                {
                    _adjacencyList[edge.SourceNodeId] = new List<int>();
                }
                if (!_adjacencyList.ContainsKey(edge.TargetNodeId))
                {
                    _adjacencyList[edge.TargetNodeId] = new List<int>();
                }

                if (!_adjacencyList[edge.SourceNodeId].Contains(edge.TargetNodeId))
                {
                    _adjacencyList[edge.SourceNodeId].Add(edge.TargetNodeId);
                }
                if (!_adjacencyList[edge.TargetNodeId].Contains(edge.SourceNodeId))
                {
                    _adjacencyList[edge.TargetNodeId].Add(edge.SourceNodeId);
                }
            }
        }

    private List<List<TransportNode>> ApplyDBSCAN(List<TransportNode> nodes)
    {
        var clusters = new List<List<TransportNode>>();
        var visited = new HashSet<int>();
        var nodesDict = nodes.ToDictionary(n => n.Id);

        // Modification : regroupement par route pour préserver la structure
        var routeGroups = _adjacencyList.GroupBy(kvp => {
            var nodeId = kvp.Key;
            var node = nodesDict.GetValueOrDefault(nodeId);
            return node?.StopId.Split('_')[0]; // Supposant que le StopId contient l'info de route
        }).Where(g => g.Key != null);

        foreach (var routeGroup in routeGroups)
        {
            foreach (var kvp in routeGroup)
            {
                var nodeId = kvp.Key;
                if (visited.Contains(nodeId))
                    continue;

                if (!nodesDict.TryGetValue(nodeId, out var node))
                    continue;

                var cluster = new List<TransportNode>();
                ExpandCluster(node, nodesDict, visited, cluster, routeGroup.Key!);

                if (cluster.Any())
                {
                    clusters.Add(cluster);
                }
            }
        }

        return clusters;
    }

    private void ExpandCluster(
        TransportNode node,
        Dictionary<int, TransportNode> nodesDict,
        HashSet<int> visited,
        List<TransportNode> cluster,
        string routePrefix)
    {
        if (!IsValidNode(node)) return;
        
        visited.Add(node.Id);
        cluster.Add(node);

        if (_adjacencyList.TryGetValue(node.Id, out var neighborIds))
        {
            foreach (var neighborId in neighborIds)
            {
                if (visited.Contains(neighborId)) continue;
                if (!nodesDict.TryGetValue(neighborId, out var neighbor)) continue;
                if (!IsValidNode(neighbor)) continue;

                // Ne regrouper que les nœuds de la même route
                if (!neighbor.StopId.StartsWith(routePrefix))
                    continue;

                if (Vector2.Distance(node.Position, neighbor.Position) <= _options.NodeClusteringDistance)
                {
                    ExpandCluster(neighbor, nodesDict, visited, cluster, routePrefix);
                }
            }
        }
    }
    
        private List<TransportNode> MergeClusters(List<List<TransportNode>> clusters)
        {
            var mergedNodes = new List<TransportNode>();

            foreach (var cluster in clusters)
            {
                if (!cluster.Any()) continue;

                var validNodes = cluster.Where(IsValidNode).ToList();
                if (!validNodes.Any()) continue;

                // Si le cluster ne contient qu'un seul nœud, on le garde tel quel
                if (validNodes.Count == 1)
                {
                    mergedNodes.Add(validNodes[0]);
                    continue;
                }

                // Calculer la position moyenne du cluster
                var centerPos = new Vector2(
                    validNodes.Average(n => n.Position.X),
                    validNodes.Average(n => n.Position.Y)
                );

                // Prendre le premier nœud comme référence pour l'identifiant
                var mainNode = validNodes[0];
                var mergedNode = new TransportNode(
                    mainNode.Id,
                    mainNode.StopId,
                    mainNode.Name,
                    centerPos,
                    validNodes.Count > 1 ? NodeType.Transfer : mainNode.Type
                );

                mergedNodes.Add(mergedNode);
            }

            return mergedNodes;
        }

        private void ApplyForceDirectedLayout(List<TransportNode> nodes)
        {
            var temperature = _geometryOptions.InitialTemperature;
            var iteration = 0;
            const int MAX_ITERATIONS = 1000;
            var nodeDict = nodes.ToDictionary(n => n.Id);

            while (temperature > _geometryOptions.StopCriterion && iteration++ < MAX_ITERATIONS)
            {
                var maxDisplacement = 0.0f;
                var forces = new Dictionary<int, Vector2>();

                // Calculer les forces pour tous les nœuds
                foreach (var node in nodes.Where(IsValidNode))
                {
                    var force = CalculateForces(node, nodeDict);
                    forces[node.Id] = force * (float)temperature;
                }

                // Appliquer les forces
                for (var i = 0; i < nodes.Count; i++)
                {
                    if (!IsValidNode(nodes[i])) continue;
                    if (!forces.TryGetValue(nodes[i].Id, out var force)) continue;

                    var displacement = force.Length();
                    maxDisplacement = Math.Max(maxDisplacement, displacement);

                    var newPosition = nodes[i].Position + force;
                    nodes[i] = nodes[i].WithPosition(newPosition);
                }

                temperature *= _geometryOptions.CoolingFactor;

                if (maxDisplacement < _geometryOptions.StopCriterion)
                    break;
            }
        }

        private Vector2 CalculateForces(TransportNode node, Dictionary<int, TransportNode> nodeDict)
        {
            if (!IsValidNode(node)) return Vector2.Zero;

            var force = Vector2.Zero;
            const float REPULSION_FACTOR = 0.1f;
            const float ATTRACTION_FACTOR = 0.15f;

            // Forces répulsives
            foreach (var otherNode in nodeDict.Values)
            {
                if (!IsValidNode(otherNode) || otherNode.Id == node.Id)
                    continue;

                var delta = node.Position - otherNode.Position;
                var distance = delta.Length();

                if (distance < EPSILON)
                    continue;

                force += Vector2.Normalize(delta) * (REPULSION_FACTOR / (distance * distance));
            }

            // Forces attractives
            if (_adjacencyList.TryGetValue(node.Id, out var neighbors))
            {
                foreach (var neighborId in neighbors)
                {
                    if (!nodeDict.TryGetValue(neighborId, out var neighbor) || !IsValidNode(neighbor))
                        continue;

                    var delta = neighbor.Position - node.Position;
                    var distance = delta.Length();

                    if (distance < EPSILON)
                        continue;

                    force += delta * ATTRACTION_FACTOR;
                }
            }

            return force;
        }

        private void AlignAngles(List<TransportNode> nodes)
        {
            var nodeDict = nodes.ToDictionary(n => n.Id);

            for (int i = 0; i < nodes.Count; i++)
            {
                if (!IsValidNode(nodes[i])) continue;
                if (!_adjacencyList.TryGetValue(nodes[i].Id, out var neighbors)) continue;

                foreach (var neighborId in neighbors)
                {
                    if (!nodeDict.TryGetValue(neighborId, out var neighbor) || !IsValidNode(neighbor))
                        continue;

                    var angle = MathF.Atan2(
                        neighbor.Position.Y - nodes[i].Position.Y,
                        neighbor.Position.X - nodes[i].Position.X
                    ) * 180 / MathF.PI;

                    var snappedAngle = MathF.Round(angle / _options.AngleSnap) * _options.AngleSnap;
                    var distance = Vector2.Distance(nodes[i].Position, neighbor.Position);

                    if (distance < EPSILON) continue;

                    var newX = nodes[i].Position.X + distance * MathF.Cos(snappedAngle * MathF.PI / 180);
                    var newY = nodes[i].Position.Y + distance * MathF.Sin(snappedAngle * MathF.PI / 180);

                    nodes[i] = nodes[i].WithPosition(new Vector2(newX, newY));
                }
            }
        }

        private void SimplifySegments(List<TransportNode> nodes)
        {
            var nodesToRemove = new HashSet<int>();
            var nodeDict = nodes.ToDictionary(n => n.Id);

            foreach (var node in nodes)
            {
                if (!IsValidNode(node)) continue;
                if (!_adjacencyList.TryGetValue(node.Id, out var neighbors) || neighbors.Count != 2)
                    continue;

                var prev = nodeDict.GetValueOrDefault(neighbors[0]);
                var next = nodeDict.GetValueOrDefault(neighbors[1]);

                if (prev == null || next == null || !IsValidNode(prev) || !IsValidNode(next))
                    continue;

                if (IsCollinear(prev.Position, node.Position, next.Position))
                {
                    nodesToRemove.Add(node.Id);
                }
            }

            nodes.RemoveAll(n => nodesToRemove.Contains(n.Id));
        }

        private bool IsCollinear(Vector2 p1, Vector2 p2, Vector2 p3)
        {
            var area = MathF.Abs(
                (p2.X - p1.X) * (p3.Y - p1.Y) -
                (p3.X - p1.X) * (p2.Y - p1.Y)
            );
            return area <= EPSILON;
        }
    }
}