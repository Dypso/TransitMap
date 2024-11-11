using System.Collections.Generic;
using System.Threading.Tasks;
using MetroMapGenerator.Core.Models;

namespace MetroMapGenerator.Core.Interfaces
{
    public interface IGraphBuilder
    {
        Task<(IEnumerable<TransportNode> nodes, IEnumerable<TransportEdge> edges)> BuildTransportGraphAsync(
            IEnumerable<TransportNode> nodes,
            IEnumerable<TransportEdge> edges);
    }
}