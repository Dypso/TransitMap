using System;
using System.Numerics;

namespace MetroMapGenerator.Core.Models
{
    // public class TransportNode
    // {
    //     public int Id { get; init; }
    //     public string StopId { get; init; }
    //     public string Name { get; init; }
    //     public Vector2 Position { get; init; }
    //     public Vector2 OriginalPosition { get; init; }
    //     public NodeType Type { get; init; }
    //     public int ConnectionCount { get; init; }
        
    //     public TransportNode(int id, string stopId, string name, Vector2 position, NodeType type = NodeType.Regular)
    //     {
    //         Id = id;
    //         StopId = stopId;
    //         Name = name;
    //         Position = position;
    //         OriginalPosition = position;
    //         Type = type;
    //         ConnectionCount = 0;
    //     }

    //     public TransportNode WithPosition(Vector2 newPosition)
    //     {
    //         return new TransportNode(Id, StopId, Name, newPosition, Type)
    //         {
    //             OriginalPosition = this.OriginalPosition,
    //             ConnectionCount = this.ConnectionCount
    //         };
    //     }

    //     public TransportNode WithType(NodeType newType)
    //     {
    //         return new TransportNode(Id, StopId, Name, Position, newType)
    //         {
    //             OriginalPosition = this.OriginalPosition,
    //             ConnectionCount = this.ConnectionCount
    //         };
    //     }

    //     public override bool Equals(object? obj)
    //     {
    //         if (obj is TransportNode other)
    //         {
    //             return Id == other.Id;
    //         }
    //         return false;
    //     }

    //     public override int GetHashCode()
    //     {
    //         return Id.GetHashCode();
    //     }
    // }

    public class TransportNode : IEquatable<TransportNode>
    {
        public int Id { get; init; }
        public string StopId { get; init; }
        public string Name { get; init; }
        public Vector2 Position { get; init; }
        public Vector2 OriginalPosition { get; init; }
        public NodeType Type { get; init; }
        public int ConnectionCount { get; init; }

        public TransportNode(int id, string stopId, string name, Vector2 position, NodeType type = NodeType.Regular)
        {
            Id = id;
            StopId = stopId ?? throw new ArgumentNullException(nameof(stopId));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Position = position;
            OriginalPosition = position;
            Type = type;
            ConnectionCount = 0;
        }

        public TransportNode WithPosition(Vector2 newPosition)
        {
            return new TransportNode(Id, StopId, Name, newPosition, Type)
            {
                OriginalPosition = this.OriginalPosition,
                ConnectionCount = this.ConnectionCount
            };
        }

        public TransportNode WithType(NodeType newType)
        {
            return new TransportNode(Id, StopId, Name, Position, newType)
            {
                OriginalPosition = this.OriginalPosition,
                ConnectionCount = this.ConnectionCount
            };
        }

        public bool Equals(TransportNode? other)
        {
            if (other is null) return false;
            return Id == other.Id;
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as TransportNode);
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        public static bool operator ==(TransportNode? left, TransportNode? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            return left.Equals(right);
        }

        public static bool operator !=(TransportNode? left, TransportNode? right)
        {
            return !(left == right);
        }
    }


    public enum NodeType
    {
        Regular,
        Transfer,
        Terminal
    }
}