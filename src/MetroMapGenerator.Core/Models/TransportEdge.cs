using System;

namespace MetroMapGenerator.Core.Models
{

    public readonly struct TransportEdge : IEquatable<TransportEdge>
    {
        public int Id { get; init; }
        public int SourceNodeId { get; init; }
        public int TargetNodeId { get; init; }
        public string RouteId { get; init; }
        public double Weight { get; init; }

        public TransportEdge(int id, int sourceNodeId, int targetNodeId, string routeId, double weight)
        {
            Id = id;
            SourceNodeId = sourceNodeId;
            TargetNodeId = targetNodeId;
            RouteId = routeId ?? throw new ArgumentNullException(nameof(routeId));
            Weight = weight;
        }
        public TransportEdge WithWeight(double newWeight)
        {
            return new TransportEdge(Id, SourceNodeId, TargetNodeId, RouteId, newWeight);
        }


        public bool Equals(TransportEdge other)
        {
            return Id == other.Id &&
                SourceNodeId == other.SourceNodeId &&
                TargetNodeId == other.TargetNodeId &&
                RouteId == other.RouteId &&
                Weight == other.Weight;
        }

        public override bool Equals(object? obj)
        {
            return obj is TransportEdge edge && Equals(edge);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Id, SourceNodeId, TargetNodeId, RouteId, Weight);
        }

        public static bool operator ==(TransportEdge left, TransportEdge right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(TransportEdge left, TransportEdge right)
        {
            return !(left == right);
        }
    }

            // public readonly struct TransportEdge
            // {
            //     public int Id { get; init; }
            //     public int SourceNodeId { get; init; }
            //     public int TargetNodeId { get; init; }
            //     public string RouteId { get; init; }
            //     public double Weight { get; init; }

            //     public TransportEdge(int id, int sourceNodeId, int targetNodeId, string routeId, double weight)
            //     {
            //         Id = id;
            //         SourceNodeId = sourceNodeId;
            //         TargetNodeId = targetNodeId;
            //         RouteId = routeId ?? throw new ArgumentNullException(nameof(routeId));
            //         Weight = weight;
            //     }

            //     public TransportEdge WithWeight(double newWeight)
            //     {
            //         return new TransportEdge(Id, SourceNodeId, TargetNodeId, RouteId, newWeight);
            //     }

            //     public override bool Equals(object? obj)
            //     {
            //         if (obj is TransportEdge other)
            //         {
            //             return Id == other.Id &&
            //                 SourceNodeId == other.SourceNodeId &&
            //                 TargetNodeId == other.TargetNodeId &&
            //                 RouteId == other.RouteId;
            //         }
            //         return false;
            //     }

            //     public override int GetHashCode()
            //     {
            //         return HashCode.Combine(Id, SourceNodeId, TargetNodeId, RouteId);
            //     }
            // }
}