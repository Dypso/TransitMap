using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using MetroMapGenerator.Core.Models;
using MetroMapGenerator.Processing.Graph;
using MetroMapGenerator.Rendering.Models;

namespace MetroMapGenerator.Rendering.SVG
{
    public partial class SVGRenderer
    {
        private Vector2 TransformPoint(Vector2 point)
        {
            return new Vector2(
                (point.X - _minBounds.X) * _scale.X + _offset.X,
                (point.Y - _minBounds.Y) * _scale.Y + _offset.Y
            );
        }

        private float CalculateStationImportance(StationInfo station)
        {
            const float TRANSFER_WEIGHT = 0.5f;
            const float CONNECTIONS_WEIGHT = 0.3f;
            const float TERMINAL_WEIGHT = 0.2f;

            var score = 0f;

            // Points pour les correspondances
            score += station.ConnectedLines.Count * TRANSFER_WEIGHT;

            // Points pour le nombre de connexions
            score += station.ConnectedStations.Count * CONNECTIONS_WEIGHT;

            // Points pour les terminus
            if (station.ConnectedStations.Count == 1)
            {
                score += TERMINAL_WEIGHT;
            }

            return score;
        }

        private float CalculateLineImportance(MetroLine line)
        {
            const float STATIONS_WEIGHT = 0.4f;
            const float TRANSFERS_WEIGHT = 0.4f;
            const float LENGTH_WEIGHT = 0.2f;

            var transferCount = line.Stations.Count(s => 
                _stationInfo.TryGetValue(s.Id, out var info) && info.ConnectedLines.Count > 1);
            
            var lineLength = CalculateLineLength(line.Segments);
            var maxLength = GetMaxLineLength();
            var normalizedLength = maxLength > 0 ? lineLength / maxLength : 0;

            return (line.Stations.Count * STATIONS_WEIGHT / 20f) +
                   (transferCount * TRANSFERS_WEIGHT / 5f) +
                   (normalizedLength * LENGTH_WEIGHT);
        }

        private float CalculateLineLength(List<LineSegment> segments)
        {
            return segments.Sum(s => Vector2.Distance(s.Start, s.End));
        }

        private float GetMaxLineLength()
        {
            return _metroLines.Values.Max(l => CalculateLineLength(l.Segments));
        }

        private Vector2 CalculateOptimalLabelPosition(TransportNode station, MetroLine line)
        {
            var info = _stationInfo[station.Id];
            float stationRadius = info.ConnectedLines.Count > 1 ? 
                STATION_RADIUS_TRANSFER : STATION_RADIUS_REGULAR;

            // Trouver la meilleure direction pour le label
            var direction = CalculateLabelDirection(station, line);
            var labelOffset = direction * (stationRadius + LABEL_OFFSET);

            // Éviter les collisions avec d'autres labels
            var initialPos = info.Position + labelOffset;
            var finalPos = AvoidLabelCollisions(initialPos, station.Id);

            // Calculer l'angle optimal du texte
            info.LabelAngle = CalculateLabelAngle(direction);

            return finalPos;
        }

        private Vector2 CalculateLabelDirection(TransportNode station, MetroLine line)
        {
            var info = _stationInfo[station.Id];
            var connectedStations = info.ConnectedStations
                .Where(id => _stationInfo.ContainsKey(id))
                .Select(id => _stationInfo[id])
                .ToList();

            if (!connectedStations.Any())
                return new Vector2(1, 0);

            // Calculer la direction moyenne des connexions
            var avgDirection = Vector2.Zero;
            foreach (var connected in connectedStations)
            {
                var dir = connected.Position - info.Position;
                if (dir != Vector2.Zero)
                {
                    avgDirection += Vector2.Normalize(dir);
                }
            }

            if (avgDirection == Vector2.Zero)
                return new Vector2(1, 0);

            // Normaliser et inverser pour placer le label à l'opposé
            return Vector2.Normalize(-avgDirection);
        }

        private Vector2 AvoidLabelCollisions(Vector2 initialPos, int stationId)
        {
            var pos = initialPos;
            var attempts = 0;
            const int MAX_ATTEMPTS = 8;
            const float ANGLE_STEP = MathF.PI / 4; // 45 degrés

            while (HasLabelCollision(pos, stationId) && attempts < MAX_ATTEMPTS)
            {
                var info = _stationInfo[stationId];
                var angle = attempts * ANGLE_STEP;
                var radius = Vector2.Distance(initialPos, info.Position);
                
                pos = info.Position + new Vector2(
                    MathF.Cos(angle) * radius,
                    MathF.Sin(angle) * radius
                );
                
                attempts++;
            }

            return pos;
        }

        private bool HasLabelCollision(Vector2 pos, int excludeStationId)
        {
            const float MIN_SQUARED_DISTANCE = MIN_LABEL_DISTANCE * MIN_LABEL_DISTANCE;

            foreach (var info in _stationInfo.Values)
            {
                if (info.Node.Id == excludeStationId) continue;

                var sqDist = Vector2.DistanceSquared(pos, info.LabelPosition);
                if (sqDist < MIN_SQUARED_DISTANCE)
                    return true;
            }

            return false;
        }

        private float CalculateLabelAngle(Vector2 direction)
        {
            var angle = MathF.Atan2(direction.Y, direction.X) * 180f / MathF.PI;
            
            // Assurer une lisibilité optimale
            if (angle > 90 || angle < -90)
            {
                angle += 180;
            }

            return angle;
        }

        private bool HasParallelLines(MetroLine line)
        {
            // Vérifier si d'autres lignes suivent un tracé similaire
            foreach (var segment in line.Segments)
            {
                foreach (var otherLine in _metroLines.Values)
                {
                    if (otherLine.Id == line.Id) continue;

                    if (HasParallelSegment(segment, otherLine.Segments))
                        return true;
                }
            }

            return false;
        }

        private bool HasParallelSegment(LineSegment segment, List<LineSegment> otherSegments)
        {
            const float PARALLEL_THRESHOLD = 0.1f;  // ~5.7 degrés
            const float DISTANCE_THRESHOLD = PARALLEL_LINE_OFFSET * 2f;

            foreach (var other in otherSegments)
            {
                // Direction vectors
                var v1 = segment.End - segment.Start;
                var v2 = other.End - other.Start;
                
                if (v1 == Vector2.Zero || v2 == Vector2.Zero) continue;

                v1 = Vector2.Normalize(v1);
                v2 = Vector2.Normalize(v2);

                // Vérifier le parallélisme
                var dot = Math.Abs(Vector2.Dot(v1, v2));
                if (dot > 1 - PARALLEL_THRESHOLD)
                {
                    // Vérifier la proximité
                    if (SegmentDistance(segment, other) < DISTANCE_THRESHOLD)
                        return true;
                }
            }

            return false;
        }

        private float SegmentDistance(LineSegment s1, LineSegment s2)
        {
            // Distance minimale entre deux segments
            var distances = new[]
            {
                PointToSegmentDistance(s1.Start, s2.Start, s2.End),
                PointToSegmentDistance(s1.End, s2.Start, s2.End),
                PointToSegmentDistance(s2.Start, s1.Start, s1.End),
                PointToSegmentDistance(s2.End, s1.Start, s1.End)
            };

            return distances.Min();
        }

        private float PointToSegmentDistance(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
        {
            var line = lineEnd - lineStart;
            var len = line.Length();
            if (len < float.Epsilon) return Vector2.Distance(point, lineStart);

            var t = Math.Clamp(Vector2.Dot(point - lineStart, line) / (len * len), 0f, 1f);
            var projection = lineStart + line * t;

            return Vector2.Distance(point, projection);
        }

        private List<string> GetParallelLines(MetroLine line)
        {
            var parallelLines = new HashSet<string>();

            foreach (var segment in line.Segments)
            {
                foreach (var otherLine in _metroLines.Values)
                {
                    if (otherLine.Id == line.Id) continue;

                    if (HasParallelSegment(segment, otherLine.Segments))
                    {
                        parallelLines.Add(otherLine.Id);
                    }
                }
            }

            return parallelLines.ToList();
        }

        private List<LineSegment> OffsetLineSegments(List<LineSegment> segments, float offset)
        {
            var offsetSegments = new List<LineSegment>();

            foreach (var segment in segments)
            {
                var direction = segment.End - segment.Start;
                if (direction == Vector2.Zero) continue;

                direction = Vector2.Normalize(direction);
                var normal = new Vector2(-direction.Y, direction.X);
                var offsetVector = normal * offset;

                offsetSegments.Add(new LineSegment
                {
                    EdgeId = segment.EdgeId,
                    SourceId = segment.SourceId,
                    TargetId = segment.TargetId,
                    RouteId = segment.RouteId,
                    Start = segment.Start + offsetVector,
                    End = segment.End + offsetVector,
                    IsTransfer = segment.IsTransfer,
                    Direction = direction
                });
            }

            return offsetSegments;
        }

        private Vector2 BezierPoint(Vector2 start, Vector2 control1, Vector2 control2, Vector2 end, float t)
        {
            var u = 1 - t;
            var tt = t * t;
            var uu = u * u;
            var uuu = uu * u;
            var ttt = tt * t;

            return uuu * start +
                   3 * uu * t * control1 +
                   3 * u * tt * control2 +
                   ttt * end;
        }
    }
}