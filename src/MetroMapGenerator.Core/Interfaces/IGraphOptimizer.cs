using System.Collections.Generic;
using MetroMapGenerator.Core.Models;

namespace MetroMapGenerator.Core.Interfaces
{
    public interface IGraphOptimizer
    {
        IEnumerable<TransportNode> OptimizeLayout(
            IEnumerable<TransportNode> nodes,
            IEnumerable<TransportEdge> edges);
    }
}