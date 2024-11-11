using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using MetroMapGenerator.Core.Models;
using MetroMapGenerator.Core.Configuration;
using System.Diagnostics;
using System.Globalization;

namespace MetroMapGenerator.Processing.GTFS
{
    public class GTFSProcessor
    {
        private readonly ProcessingOptions _options;
        private readonly GTFSReader _reader;
        private readonly Dictionary<string, List<string>> _stopConnections;
        private readonly Dictionary<string, HashSet<string>> _routeStops;
        private int _lastNodeId = 0;

        private async Task<T> ReadWithProgress<T>(Task<T> task, string dataType) where T : class
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            
            Console.WriteLine($"Reading {dataType}...");
            var result = await task;
            Console.WriteLine($"Completed reading {dataType}.");
            return result;
        }

        public GTFSProcessor(ProcessingOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _reader = new GTFSReader();
            _stopConnections = new Dictionary<string, List<string>>();
            _routeStops = new Dictionary<string, HashSet<string>>();
        }

        public async Task<(IEnumerable<TransportNode> nodes, IEnumerable<TransportEdge> edges)> ProcessFilesAsync(
            string routesPath,
            string tripsPath,
            string stopsPath,
            string stopTimesPath)
        {


            if (string.IsNullOrEmpty(routesPath)) throw new ArgumentNullException(nameof(routesPath));
            if (string.IsNullOrEmpty(tripsPath)) throw new ArgumentNullException(nameof(tripsPath));
            if (string.IsNullOrEmpty(stopsPath)) throw new ArgumentNullException(nameof(stopsPath));
            if (string.IsNullOrEmpty(stopTimesPath)) throw new ArgumentNullException(nameof(stopTimesPath));


            var stopwatch = new Stopwatch();
            stopwatch.Start();
            Console.WriteLine("Starting GTFS file processing...");

            try
            {
                // Parallel file reading with progress tracking
                var stopTask = ReadWithProgress(_reader.ReadStopsAsync(stopsPath), "Stops");
                var routeTask = ReadWithProgress(_reader.ReadRoutesAsync(routesPath), "Routes");
                var tripTask = ReadWithProgress(_reader.ReadTripsAsync(tripsPath), "Trips");
                var stopTimeTask = ReadWithProgress(_reader.ReadStopTimesAsync(stopTimesPath), "Stop Times");

                await Task.WhenAll(stopTask, routeTask, tripTask, stopTimeTask);

                var stops = await stopTask;
                var routes = await routeTask;
                var trips = await tripTask;
                var stopTimes = await stopTimeTask;

                if (stops == null || routes == null || trips == null || stopTimes == null)
                {
                    throw new InvalidOperationException("Failed to read one or more GTFS files");
                }                

                ValidateGTFSData(stops, routes, trips, stopTimes);

                Console.WriteLine($"Building stop connections... Time elapsed: {stopwatch.Elapsed.TotalSeconds:F2}s");
                await BuildStopConnectionsAsync(stopTimes, trips);
                
                Console.WriteLine($"Creating network nodes... Time elapsed: {stopwatch.Elapsed.TotalSeconds:F2}s");
                var nodes = CreateNodes(stops);
                
                Console.WriteLine($"Creating network edges... Time elapsed: {stopwatch.Elapsed.TotalSeconds:F2}s");
                var edges = CreateEdges(nodes);

                // Log processing results
                LogProcessingResults(nodes, edges, stopwatch.Elapsed);

                return (nodes, edges);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"Error processing GTFS files: {ex.Message}");
                Console.Error.WriteLine(ex.StackTrace);
                Console.ResetColor();
                throw;
            }
        }



        private void ValidateGTFSData(
            IEnumerable<Stop> stops,
            IEnumerable<Route> routes,
            IEnumerable<Trip> trips,
            IEnumerable<StopTime> stopTimes)
        {
            if (stops is null || !stops.Any())
                throw new InvalidOperationException("No stops found in GTFS data.");
            if (routes is null || !routes.Any())
                throw new InvalidOperationException("No routes found in GTFS data.");
            if (trips is null || !trips.Any())
                throw new InvalidOperationException("No trips found in GTFS data.");
            if (stopTimes is null || !stopTimes.Any())
                throw new InvalidOperationException("No stop times found in GTFS data.");

            // Validate coordinate ranges
            foreach (var stop in stops.Where(s => s != null))  // Ajout d'une vérification null
            {
                if (!IsValidCoordinate(stop.Latitude, stop.Longitude))
                {
                    Console.WriteLine($"Warning: Invalid coordinates for stop {stop.StopId}: ({stop.Latitude}, {stop.Longitude})");
                }
            }
        }

        private bool IsValidCoordinate(double lat, double lon)
        {
            return !double.IsNaN(lat) && !double.IsInfinity(lat) &&
                   !double.IsNaN(lon) && !double.IsInfinity(lon) &&
                   lat >= -90 && lat <= 90 && lon >= -180 && lon <= 180;
        }

        private async Task BuildStopConnectionsAsync(IEnumerable<StopTime> stopTimes, IEnumerable<Trip> trips)
        {
            await Task.Run(() =>
            {
                var tripDict = trips.ToDictionary(t => t.TripId, t => t.RouteId);
                var stopTimesByTrip = stopTimes.GroupBy(st => st.TripId).ToDictionary(g => g.Key, g => g.ToList());

                foreach (var tripGroup in stopTimesByTrip)
                {
                    if (!tripDict.TryGetValue(tripGroup.Key, out var routeId))
                        continue;

                    var orderedStops = tripGroup.Value
                        .OrderBy(st => st.StopSequence)
                        .Select(st => st.StopId)
                        .ToList();

                    // Add stops to route
                    if (!_routeStops.ContainsKey(routeId))
                        _routeStops[routeId] = new HashSet<string>();
                    
                    foreach (var stopId in orderedStops)
                    {
                        _routeStops[routeId].Add(stopId);
                    }

                    // Build connections
                    for (int i = 0; i < orderedStops.Count - 1; i++)
                    {
                        var currentStop = orderedStops[i];
                        var nextStop = orderedStops[i + 1];

                        if (!_stopConnections.ContainsKey(currentStop))
                        {
                            _stopConnections[currentStop] = new List<string>();
                        }

                        if (!_stopConnections[currentStop].Contains(nextStop))
                        {
                            _stopConnections[currentStop].Add(nextStop);
                        }
                    }
                }

                Console.WriteLine($"Built {_stopConnections.Count:N0} stop connections across {_routeStops.Count:N0} routes");
            });
        }

        private IEnumerable<TransportNode> CreateNodes(IEnumerable<Stop> stops)
        {
            var nodes = new List<TransportNode>();
            var transferStops = FindTransferStops();

            foreach (var stop in stops)
            {
                // Validation plus stricte des coordonnées
                if (double.IsNaN(stop.Latitude) || double.IsNaN(stop.Longitude) ||
                    double.IsInfinity(stop.Latitude) || double.IsInfinity(stop.Longitude) ||
                    stop.Latitude == 0 || stop.Longitude == 0)
                {
                    Console.WriteLine($"Warning: Invalid coordinates for stop {stop.StopId}: ({stop.Latitude}, {stop.Longitude})");
                    continue;
                }

                var position = new Vector2(
                    (float)stop.Longitude,
                    (float)stop.Latitude
                );

                // Vérification supplémentaire après la conversion en Vector2
                if (float.IsNaN(position.X) || float.IsNaN(position.Y))
                {
                    Console.WriteLine($"Warning: Invalid Vector2 conversion for stop {stop.StopId}");
                    continue;
                }

                var nodeType = transferStops.Contains(stop.StopId) ? 
                    NodeType.Transfer : 
                    NodeType.Regular;

                var node = new TransportNode(
                    _lastNodeId++,
                    stop.StopId,
                    stop.Name,
                    position,
                    nodeType
                );

                // Vérification finale du nœud
                if (node.Position.X != position.X || node.Position.Y != position.Y)
                {
                    Console.WriteLine($"Warning: Position mismatch for node {node.Id}");
                    continue;
                }

                nodes.Add(node);
            }

            Console.WriteLine($"Created {nodes.Count:N0} nodes ({nodes.Count(n => n.Type == NodeType.Transfer):N0} transfer stations)");
            
            // Log des coordonnées min/max pour vérification
            if (nodes.Any())
            {
                var minX = nodes.Min(n => n.Position.X);
                var maxX = nodes.Max(n => n.Position.X);
                var minY = nodes.Min(n => n.Position.Y);
                var maxY = nodes.Max(n => n.Position.Y);
                Console.WriteLine($"Node coordinate ranges: X({minX:F6} to {maxX:F6}), Y({minY:F6} to {maxY:F6})");
            }

            return nodes;
        }

        private HashSet<string> FindTransferStops()
        {
            var transferStops = new HashSet<string>();
            
            foreach (var stop in _stopConnections.Keys)
            {
                var routesServingStop = _routeStops
                    .Where(r => r.Value.Contains(stop))
                    .Select(r => r.Key)
                    .ToList();

                if (routesServingStop.Count > 1)
                {
                    transferStops.Add(stop);
                }
            }

            return transferStops;
        }

        private IEnumerable<TransportEdge> CreateEdges(IEnumerable<TransportNode> nodes)
        {
            var edges = new List<TransportEdge>();
            var nodeDict = nodes.ToDictionary(n => n.StopId, n => n.Id);
            var edgeId = 0;

            foreach (var connection in _stopConnections)
            {
                var sourceStopId = connection.Key;
                foreach (var targetStopId in connection.Value)
                {
                    if (nodeDict.TryGetValue(sourceStopId, out var sourceNodeId) && 
                        nodeDict.TryGetValue(targetStopId, out var targetNodeId))
                    {
                        var sourceNode = nodes.First(n => n.Id == sourceNodeId);
                        var targetNode = nodes.First(n => n.Id == targetNodeId);
                        
                        var distance = Vector2.Distance(sourceNode.Position, targetNode.Position);
                        var weight = CalculateEdgeWeight(distance);

                        // Find the route(s) that contain this connection
                        var routeId = FindRouteForConnection(sourceStopId, targetStopId);

                        var edge = new TransportEdge(
                            edgeId++,
                            sourceNodeId,
                            targetNodeId,
                            routeId,
                            weight
                        );
                        edges.Add(edge);
                    }
                }
            }

            Console.WriteLine($"Created {edges.Count:N0} edges");
            return edges;
        }

        private string FindRouteForConnection(string sourceStopId, string targetStopId)
        {
            foreach (var route in _routeStops)
            {
                if (route.Value.Contains(sourceStopId) && route.Value.Contains(targetStopId))
                {
                    return route.Key;
                }
            }
            return "default";
        }

        private double CalculateEdgeWeight(float distance)
        {
            const float minWeight = 0.1f;
            const float maxWeight = 1.0f;
            const float normalizer = 0.1f;
            
            return Math.Max(minWeight, Math.Min(maxWeight, distance / normalizer));
        }

        private void LogProcessingResults(IEnumerable<TransportNode> nodes, IEnumerable<TransportEdge> edges, TimeSpan elapsed)
        {
            var nodesList = nodes.ToList();
            var edgesList = edges.ToList();

            Console.WriteLine("\nGTFS Processing Summary:");
            Console.WriteLine($"Total processing time: {elapsed.TotalSeconds:F2} seconds");
            Console.WriteLine($"Nodes created: {nodesList.Count:N0}");
            Console.WriteLine($"- Transfer stations: {nodesList.Count(n => n.Type == NodeType.Transfer):N0}");
            Console.WriteLine($"- Regular stations: {nodesList.Count(n => n.Type == NodeType.Regular):N0}");
            Console.WriteLine($"Edges created: {edgesList.Count:N0}");
            Console.WriteLine($"Routes processed: {_routeStops.Count:N0}");
            
            // Network density metrics
            var density = (double)edgesList.Count / (nodesList.Count * (nodesList.Count - 1));
            Console.WriteLine($"Network density: {density:P2}");
        }
    }
}