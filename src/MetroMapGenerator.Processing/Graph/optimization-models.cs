using System;
using System.Collections.Generic;
using System.Numerics;
using MetroMapGenerator.Core.Models;

namespace MetroMapGenerator.Rendering.Models
{
    public class LineSegment
    {
        public required int EdgeId { get; init; }
        public required int SourceId { get; init; }
        public required int TargetId { get; init; }
        public required string RouteId { get; init; }
        public required Vector2 Start { get; init; }
        public required Vector2 End { get; init; }
        public bool IsTransfer { get; init; }
        public float Angle { get; init; }
        public List<Vector2> ControlPoints { get; init; } = new();
        public Vector2 Direction { get; set; }
        public float Length { get; set; }
    }

    public class RouteMetrics
    {
        public required string RouteId { get; init; }
        public int StationCount { get; set; }
        public int TransferCount { get; set; }
        public float TotalLength { get; set; }
        public float ImportanceScore { get; private set; }
        public bool IsMainLine { get; set; }

        public void CalculateImportanceScore(float maxLength, int maxStations, int maxTransfers)
        {
            const float LENGTH_WEIGHT = 0.4f;
            const float STATIONS_WEIGHT = 0.3f;
            const float TRANSFERS_WEIGHT = 0.3f;

            ImportanceScore = 
                (TotalLength / maxLength) * LENGTH_WEIGHT +
                (StationCount / (float)maxStations) * STATIONS_WEIGHT +
                (TransferCount / (float)maxTransfers) * TRANSFERS_WEIGHT;
        }
    }

    public class MetroLine
    {
        public required string Id { get; init; }
        public required string Color { get; init; }
        public required string Name { get; init; }
        public List<LineSegment> Segments { get; init; } = new();
        public List<TransportNode> Stations { get; init; } = new();
        public float Importance { get; set; }
    }

    public class StationInfo
    {
        public required TransportNode Node { get; init; }
        public HashSet<string> ConnectedLines { get; init; } = new();
        public HashSet<int> ConnectedStations { get; init; } = new();
        public float Importance { get; set; }
        public Vector2 LabelPosition { get; set; }
        public float LabelAngle { get; set; }
        public Vector2 Position { get; set; }
    }

    public sealed class TransitLine
    {
        private float _importanceScore;

        public required string RouteId { get; init; }
        public required List<TransportNode> Nodes { get; init; } = new();
        public required List<LineSegment> Segments { get; init; } = new();
        public required string Color { get; init; }
        public required string Name { get; init; }

        public float ImportanceScore 
        { 
            get => _importanceScore;
            init => _importanceScore = value;
        }

        public TransitLine()
        {
            _importanceScore = 0;
        }

        public static TransitLine Create(
            string routeId,
            List<TransportNode> nodes,
            List<LineSegment> segments,
            string color,
            string name,
            float importanceScore)
        {
            return new TransitLine
            {
                RouteId = routeId,
                Nodes = nodes,
                Segments = segments,
                Color = color,
                Name = name,
                ImportanceScore = importanceScore
            };
        }
    }

    public class RouteInfo
    {
        private float _importanceScore;

        public required string RouteId { get; init; }
        public List<TransportNode> Nodes { get; init; } = new();
        public List<LineSegment> Segments { get; init; } = new();
        public int StationCount { get; set; }
        public int TransferCount { get; set; }
        public float TotalLength { get; set; }
        public bool IsMainLine { get; set; }

        public float ImportanceScore
        {
            get => _importanceScore;
            private set => _importanceScore = value;
        }

        public void CalculateImportanceScore(float maxLength, int maxStations, int maxTransfers)
        {
            const float LENGTH_WEIGHT = 0.4f;
            const float STATIONS_WEIGHT = 0.3f;
            const float TRANSFERS_WEIGHT = 0.3f;

            _importanceScore = 
                (TotalLength / maxLength) * LENGTH_WEIGHT +
                (StationCount / (float)maxStations) * STATIONS_WEIGHT +
                (TransferCount / (float)maxTransfers) * TRANSFERS_WEIGHT;
        }
    }
}