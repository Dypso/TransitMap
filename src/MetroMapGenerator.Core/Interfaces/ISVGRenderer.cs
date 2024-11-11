using System.Threading.Tasks;
using System.Collections.Generic;
using MetroMapGenerator.Core.Models;
using MetroMapGenerator.Core.Configuration;

namespace MetroMapGenerator.Core.Interfaces
{
    public interface ISVGRenderer
    {
        Task<string> RenderMapAsync(
            IEnumerable<TransportNode> nodes,
            IEnumerable<TransportEdge> edges,
            RenderingOptions options);
    }
}