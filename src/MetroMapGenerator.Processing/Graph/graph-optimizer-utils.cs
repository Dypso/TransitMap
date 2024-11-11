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
        private List<TransportNode> NormalizeCoordinates(List<TransportNode> nodes)
        {
            if (!nodes.Any()) return nodes;

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

            if (width < float.Epsilon || height < float.Epsilon)
            {
                throw new InvalidOperationException("Node coordinates are too close together for meaningful normalization");
            }

            _scale = new Vector2(1.0f / width, 1.0f / height);

            return nodes.Select(node => new TransportNode(
                node.Id,
                node.StopId,
                node.Name,
                new Vector2(
                    (node.Position.X - _minBounds.X) * _scale.X,
                    (node.Position.Y - _minBounds.Y) * _scale.Y
                ),
                node.Type
            )).ToList();
        }

        private List<TransportNode> DenormalizeCoordinates(List<TransportNode> nodes)
        {
            if (!nodes.Any()) return nodes;

            return nodes.Select(node => new TransportNode(
                node.Id,
                node.StopId,
                node.Name,
                new Vector2(
                    node.Position.X / _scale.X + _minBounds.X,
                    node.Position.Y / _scale.Y + _minBounds.Y
                ),
                node.Type
            )).ToList();
        }

        private List<TransportNode> GetRouteNodes(List<TransportEdge> edges, List<TransportNode> allNodes)
        {
            var nodeIds = new HashSet<int>();
            foreach (var edge in edges)
            {
                nodeIds.Add(edge.SourceNodeId);
                nodeIds.Add(edge.TargetNodeId);
            }

            return allNodes.Where(n => nodeIds.Contains(n.Id)).ToList();
        }

        private List<LineSegment> BuildLineSegments(List<TransportEdge> edges, List<TransportNode> nodes)
        {
            var nodeDict = nodes.ToDictionary(n => n.Id);
            var segments = new List<LineSegment>();

            foreach (var edge in edges)
            {
                if (!nodeDict.TryGetValue(edge.SourceNodeId, out var sourceNode) ||
                    !nodeDict.TryGetValue(edge.TargetNodeId, out var targetNode))
                    continue;

                var segment = new LineSegment
                {
                    EdgeId = edge.Id,
                    SourceId = edge.SourceNodeId,
                    TargetId = edge.TargetNodeId,
                    RouteId = edge.RouteId,
                    Start = TransformPoint(sourceNode.Position),
                    End = TransformPoint(targetNode.Position),
                    IsTransfer = sourceNode.Type == NodeType.Transfer || targetNode.Type == NodeType.Transfer,
                    Direction = Vector2.Normalize(targetNode.Position - sourceNode.Position),
                    Length = Vector2.Distance(sourceNode.Position, targetNode.Position)
                };

                segments.Add(segment);
            }

            return segments;
        }

        private Vector2 TransformPoint(Vector2 point)
        {
            return new Vector2(
                (point.X - _minBounds.X) * _scale.X,
                (point.Y - _minBounds.Y) * _scale.Y
            );
        }

        private float CalculateLineImportance(List<TransportNode> nodes)
        {
            const float TRANSFER_WEIGHT = 0.4f;
            const float STATIONS_WEIGHT = 0.3f;
            const float LENGTH_WEIGHT = 0.3f;

            var transferCount = nodes.Count(n => n.Type == NodeType.Transfer);
            var stationCount = nodes.Count;
            var length = CalculateLineLength(nodes);

            return (transferCount * TRANSFER_WEIGHT +
                   stationCount * STATIONS_WEIGHT +
                   length * LENGTH_WEIGHT) / 10f;
        }

        private float CalculateLineLength(List<TransportNode> nodes)
        {
            float length = 0;
            for (int i = 0; i < nodes.Count - 1; i++)
            {
                length += Vector2.Distance(nodes[i].Position, nodes[i + 1].Position);
            }
            return length;
        }

        private List<TransportNode> SimplifyLine(List<TransportNode> nodes)
        {
            if (nodes.Count < 3) return nodes;

            var simplified = new List<TransportNode> { nodes[0] };
            var angleThreshold = OCTILINEAR_ANGLE * 0.5f * MathF.PI / 180f;

            for (int i = 1; i < nodes.Count - 1; i++)
            {
                var prev = nodes[i - 1];
                var curr = nodes[i];
                var next = nodes[i + 1];

                var angle = CalculateAngle(prev.Position, curr.Position, next.Position);
                
                if (MathF.Abs(angle - MathF.PI) > angleThreshold || curr.Type == NodeType.Transfer)
                {
                    simplified.Add(curr);
                }
            }

            simplified.Add(nodes[nodes.Count - 1]);
            return simplified;
        }

        private float CalculateAngle(Vector2 p1, Vector2 p2, Vector2 p3)
        {
            var v1 = Vector2.Normalize(p1 - p2);
            var v2 = Vector2.Normalize(p3 - p2);
            return MathF.Acos(Vector2.Dot(v1, v2));
        }

        private Vector2 SnapToOctilinearAngle(Vector2 direction)
        {
            var angle = MathF.Atan2(direction.Y, direction.X);
            var snappedAngle = MathF.Round(angle / (OCTILINEAR_ANGLE * MathF.PI / 180f)) 
                              * (OCTILINEAR_ANGLE * MathF.PI / 180f);
            
            return new Vector2(
                MathF.Cos(snappedAngle),
                MathF.Sin(snappedAngle)
            );
        }

        private Vector2 SnapToGrid(Vector2 position)
        {
            return new Vector2(
                MathF.Round(position.X / GRID_SIZE) * GRID_SIZE,
                MathF.Round(position.Y / GRID_SIZE) * GRID_SIZE
            );
        }

        private List<string> FindLineVariants(string mainRouteId, Dictionary<string, List<TransportNode>> routeNodes)
        {
            return routeNodes.Keys
                .Where(k => k != mainRouteId && k.StartsWith(mainRouteId))
                .ToList();
        }

        private Vector2 CalculateSpacingForce(TransportNode prev, TransportNode curr, TransportNode next)
        {
            var force = Vector2.Zero;
            var prevDist = Vector2.Distance(curr.Position, prev.Position);
            var nextDist = Vector2.Distance(curr.Position, next.Position);

            if (prevDist < MIN_STATION_DISTANCE)
            {
                force += Vector2.Normalize(curr.Position - prev.Position) 
                        * (MIN_STATION_DISTANCE - prevDist);
            }

            if (nextDist < MIN_STATION_DISTANCE)
            {
                force += Vector2.Normalize(curr.Position - next.Position) 
                        * (MIN_STATION_DISTANCE - nextDist);
            }

            return force;
        }

        private float CalculateNodeImportance(TransportNode node, HashSet<string> connectedLines)
        {
            const float TRANSFER_WEIGHT = 0.5f;
            const float TERMINAL_WEIGHT = 0.3f;
            const float CONNECTIONS_WEIGHT = 0.2f;

            var score = 0f;

            if (node.Type == NodeType.Transfer)
            {
                score += TRANSFER_WEIGHT * connectedLines.Count;
            }

            if (node.Type == NodeType.Terminal)
            {
                score += TERMINAL_WEIGHT;
            }

            score += CONNECTIONS_WEIGHT * node.ConnectionCount;

            return score;
        }
    }
}