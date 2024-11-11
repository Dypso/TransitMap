using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using MetroMapGenerator.Core.Models;
using MetroMapGenerator.Rendering.Models;

namespace MetroMapGenerator.Processing.Graph
{
    public partial class GraphOptimizer
    {
        private List<TransportNode> OptimizeSchematicLayout(List<TransportNode> nodes, List<TransitLine> mainLines)
        {
            var optimizedNodes = new Dictionary<int, TransportNode>(nodes.ToDictionary(n => n.Id));
            var iteration = 0;
            var maxMovement = float.MaxValue;

            while (iteration++ < MAX_OPTIMIZATION_ITERATIONS && maxMovement > CONVERGENCE_THRESHOLD)
            {
                maxMovement = 0f;

                // 1. Optimisation des lignes principales
                foreach (var line in mainLines.OrderByDescending(l => l.ImportanceScore))
                {
                    var movement = OptimizeLine(line, optimizedNodes);
                    maxMovement = Math.Max(maxMovement, movement);
                }

                // 2. Optimisation des stations de correspondance
                var transferMovement = OptimizeTransferStations(optimizedNodes, mainLines);
                maxMovement = Math.Max(maxMovement, transferMovement);

                // 3. Résolution des conflits de positionnement
                var conflictMovement = ResolvePositionConflicts(optimizedNodes);
                maxMovement = Math.Max(maxMovement, conflictMovement);

                // 4. Maintien de l'espacement minimal
                var spacingMovement = AdjustStationSpacing(optimizedNodes);
                maxMovement = Math.Max(maxMovement, spacingMovement);

                Console.WriteLine($"Iteration {iteration}: Max movement = {maxMovement:F3}");
            }

            return optimizedNodes.Values.ToList();
        }

        private float OptimizeLine(TransitLine line, Dictionary<int, TransportNode> nodes)
        {
            var maxMovement = 0f;
            var orderedNodes = OrderNodesAlongLine(line.Nodes, nodes);

            // Optimisation des segments
            for (int i = 1; i < orderedNodes.Count - 1; i++)
            {
                var prev = nodes[orderedNodes[i - 1].Id];
                var curr = nodes[orderedNodes[i].Id];
                var next = nodes[orderedNodes[i + 1].Id];

                var newPos = CalculateOptimalPosition(prev, curr, next);
                var movement = Vector2.Distance(newPos, curr.Position);

                if (movement > CONVERGENCE_THRESHOLD)
                {
                    nodes[curr.Id] = curr.WithPosition(newPos);
                    maxMovement = Math.Max(maxMovement, movement);
                }
            }

            return maxMovement;
        }

        private List<TransportNode> OrderNodesAlongLine(List<TransportNode> lineNodes, Dictionary<int, TransportNode> currentPositions)
        {
            var ordered = new List<TransportNode>();
            var remaining = new HashSet<TransportNode>(lineNodes);

            // Commencer par un terminal
            var start = lineNodes.FirstOrDefault(n => n.Type == NodeType.Terminal) ?? lineNodes.First();
            ordered.Add(start);
            remaining.Remove(start);

            while (remaining.Any())
            {
                var last = ordered.Last();
                var next = remaining
                    .OrderBy(n => Vector2.Distance(currentPositions[n.Id].Position, currentPositions[last.Id].Position))
                    .First();

                ordered.Add(next);
                remaining.Remove(next);
            }

            return ordered;
        }

        private Vector2 CalculateOptimalPosition(TransportNode prev, TransportNode curr, TransportNode next)
        {
            var dirPrev = Vector2.Normalize(curr.Position - prev.Position);
            var dirNext = Vector2.Normalize(next.Position - curr.Position);

            // Aligner sur les angles octilinéaires
            var alignedDirPrev = SnapToOctilinearAngle(dirPrev);
            var alignedDirNext = SnapToOctilinearAngle(dirNext);

            // Force d'alignement
            var alignmentForce = (alignedDirPrev + alignedDirNext) * STRAIGHTNESS_WEIGHT;
            var optimalPos = curr.Position + alignmentForce;

            // Force d'espacement
            var spacingForce = CalculateSpacingForce(prev, curr, next) * SPACING_WEIGHT;
            optimalPos += spacingForce;

            return SnapToGrid(optimalPos);
        }

        private float OptimizeTransferStations(Dictionary<int, TransportNode> nodes, List<TransitLine> lines)
        {
            var maxMovement = 0f;
            var transferNodes = nodes.Values.Where(n => n.Type == NodeType.Transfer);

            foreach (var node in transferNodes)
            {
                var connectedLines = lines.Where(l => l.Nodes.Any(n => n.Id == node.Id)).ToList();
                if (connectedLines.Count < 2) continue;

                var newPos = CalculateTransferPosition(node, connectedLines, nodes);
                var movement = Vector2.Distance(newPos, node.Position);

                if (movement > CONVERGENCE_THRESHOLD)
                {
                    nodes[node.Id] = node.WithPosition(newPos);
                    maxMovement = Math.Max(maxMovement, movement);
                }
            }

            return maxMovement;
        }

        private Vector2 CalculateTransferPosition(TransportNode node, List<TransitLine> connectedLines, Dictionary<int, TransportNode> allNodes)
        {
            var optimalPos = Vector2.Zero;
            var totalWeight = 0f;

            foreach (var line in connectedLines)
            {
                var lineNodes = line.Nodes.Where(n => n.Id != node.Id)
                    .Select(n => allNodes[n.Id])
                    .ToList();

                if (!lineNodes.Any()) continue;

                var lineCenter = CalculateLineCenter(lineNodes);
                var weight = line.ImportanceScore;

                optimalPos += lineCenter * weight;
                totalWeight += weight;
            }

            if (totalWeight > 0)
            {
                optimalPos /= totalWeight;
                return SnapToGrid(optimalPos);
            }

            return node.Position;
        }

        private Vector2 CalculateLineCenter(List<TransportNode> nodes)
        {
            if (!nodes.Any()) return Vector2.Zero;
            return new Vector2(
                nodes.Average(n => n.Position.X),
                nodes.Average(n => n.Position.Y)
            );
        }

        private float ResolvePositionConflicts(Dictionary<int, TransportNode> nodes)
        {
            var maxMovement = 0f;
            var nodesList = nodes.Values.ToList();

            for (int i = 0; i < nodesList.Count; i++)
            {
                for (int j = i + 1; j < nodesList.Count; j++)
                {
                    var node1 = nodesList[i];
                    var node2 = nodesList[j];
                    var distance = Vector2.Distance(node1.Position, node2.Position);

                    if (distance < MIN_STATION_DISTANCE)
                    {
                        var movement = ResolveNodeConflict(nodes, node1, node2);
                        maxMovement = Math.Max(maxMovement, movement);
                    }
                }
            }

            return maxMovement;
        }

        private float ResolveNodeConflict(Dictionary<int, TransportNode> nodes, TransportNode node1, TransportNode node2)
        {
            var direction = Vector2.Normalize(node2.Position - node1.Position);
            if (direction == Vector2.Zero)
            {
                direction = new Vector2(1, 0);
            }

            var separation = MIN_STATION_DISTANCE * 1.1f;
            var movement = 0f;

            if (node1.Type == NodeType.Transfer && node2.Type != NodeType.Transfer)
            {
                var newPos = node1.Position + direction * separation;
                nodes[node2.Id] = node2.WithPosition(newPos);
                movement = separation;
            }
            else if (node2.Type == NodeType.Transfer && node1.Type != NodeType.Transfer)
            {
                var newPos = node2.Position - direction * separation;
                nodes[node1.Id] = node1.WithPosition(newPos);
                movement = separation;
            }
            else
            {
                var offset = direction * (separation * 0.5f);
                nodes[node1.Id] = node1.WithPosition(node1.Position - offset);
                nodes[node2.Id] = node2.WithPosition(node2.Position + offset);
                movement = separation * 0.5f;
            }

            return movement;
        }

        private float AdjustStationSpacing(Dictionary<int, TransportNode> nodes)
        {
            var maxMovement = 0f;
            var nodesList = nodes.Values.ToList();

            for (int i = 0; i < nodesList.Count; i++)
            {
                var node = nodesList[i];
                var nearbyNodes = FindNearbyNodes(node, nodesList, MIN_STATION_DISTANCE * 2);

                foreach (var nearby in nearbyNodes)
                {
                    var distance = Vector2.Distance(node.Position, nearby.Position);
                    if (distance < MIN_STATION_DISTANCE)
                    {
                        var movement = AdjustNodePairSpacing(nodes, node, nearby);
                        maxMovement = Math.Max(maxMovement, movement);
                    }
                }
            }

            return maxMovement;
        }

        private float AdjustNodePairSpacing(Dictionary<int, TransportNode> nodes, TransportNode node1, TransportNode node2)
        {
            var direction = Vector2.Normalize(node2.Position - node1.Position);
            var currentDistance = Vector2.Distance(node1.Position, node2.Position);
            var adjustment = (MIN_STATION_DISTANCE - currentDistance) * 0.5f;

            var newPos1 = node1.Position - direction * adjustment;
            var newPos2 = node2.Position + direction * adjustment;

            nodes[node1.Id] = node1.WithPosition(newPos1);
            nodes[node2.Id] = node2.WithPosition(newPos2);

            return Math.Abs(adjustment);
        }

        private IEnumerable<TransportNode> FindNearbyNodes(TransportNode node, List<TransportNode> allNodes, float radius)
        {
            return allNodes.Where(n => 
                n.Id != node.Id && 
                Vector2.Distance(n.Position, node.Position) <= radius);
        }

        private List<TransportNode> FinalizeLayout(List<TransportNode> nodes)
        {
            return nodes.Where(n => 
                !float.IsNaN(n.Position.X) && 
                !float.IsInfinity(n.Position.X) &&
                !float.IsNaN(n.Position.Y) && 
                !float.IsInfinity(n.Position.Y)
            ).ToList();
        }
    }
}