using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MetroMapGenerator.Core.Configuration;
using MetroMapGenerator.Core.Interfaces;
using MetroMapGenerator.Processing.GTFS;
using MetroMapGenerator.Processing.Graph;
using MetroMapGenerator.Processing.Topology;
using MetroMapGenerator.Rendering.SVG;
using System.Linq;
using System.Diagnostics;

namespace MetroMapGenerator
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            try
            {
                var services = ConfigureServices();
                await ProcessMetroMapAsync(services, args);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"Critical Error: {ex.Message}");
                Console.Error.WriteLine(ex.StackTrace);
                Console.ResetColor();
                Environment.Exit(1);
            }
        }

        private static async Task ProcessMetroMapAsync(IServiceProvider services, string[] args)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            if (!ValidateInputFiles(args))
            {
                return;
            }

            using var scope = services.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<GTFSProcessor>();
            var graphBuilder = scope.ServiceProvider.GetRequiredService<IGraphBuilder>();
            var graphOptimizer = scope.ServiceProvider.GetRequiredService<IGraphOptimizer>();
            var svgRenderer = scope.ServiceProvider.GetRequiredService<ISVGRenderer>();
            var topologyOptimizer = scope.ServiceProvider.GetRequiredService<TopologyOptimizer>();

            Console.WriteLine("\n=== Starting GTFS Processing ===");
            Console.WriteLine($"Processing GTFS files from: {Path.GetDirectoryName(args[0])}");
            var (initialNodes, initialEdges) = await processor.ProcessFilesAsync(
                args[0], // routes.txt
                args[1], // trips.txt
                args[2], // stops.txt
                args[3]  // stop_times.txt
            );

            if (initialNodes == null || !initialNodes.Any())
            {
                throw new InvalidOperationException("No valid nodes were processed from GTFS files.");
            }

            if (initialEdges == null || !initialEdges.Any())
            {
                throw new InvalidOperationException("No valid edges were processed from GTFS files.");
            }

            LogProcessingStep("GTFS Processing", initialNodes.Count(), initialEdges.Count(), stopwatch.Elapsed);

            Console.WriteLine("\n=== Building Transport Graph ===");
            var (optimizedNodes, optimizedEdges) = await graphBuilder.BuildTransportGraphAsync(initialNodes, initialEdges);

            if (optimizedNodes == null || !optimizedNodes.Any())
            {
                throw new InvalidOperationException("Graph builder produced no valid nodes.");
            }

            if (optimizedEdges == null)
            {
                optimizedEdges = initialEdges;
            }

            LogProcessingStep("Graph Building", optimizedNodes.Count(), optimizedEdges.Count(), stopwatch.Elapsed);

            Console.WriteLine("\n=== Optimizing Layout ===");
            var layoutOptimizedNodes = graphOptimizer.OptimizeLayout(optimizedNodes, optimizedEdges).ToList();

            if (layoutOptimizedNodes == null || !layoutOptimizedNodes.Any())
            {
                throw new InvalidOperationException("Layout optimization produced no valid nodes.");
            }

            LogProcessingStep("Layout Optimization", layoutOptimizedNodes.Count, optimizedEdges.Count(), stopwatch.Elapsed);

            Console.WriteLine("\n=== Optimizing Topology ===");
            var finalNodes = topologyOptimizer.OptimizeTopology(layoutOptimizedNodes, optimizedEdges).ToList();

            if (finalNodes == null || !finalNodes.Any())
            {
                throw new InvalidOperationException("Topology optimization produced no valid nodes.");
            }

            LogProcessingStep("Topology Optimization", finalNodes.Count, optimizedEdges.Count(), stopwatch.Elapsed);

            Console.WriteLine("\n=== Rendering SVG ===");
            var renderingOptions = new RenderingOptions();
            var svgContent = await svgRenderer.RenderMapAsync(finalNodes, optimizedEdges, renderingOptions);

            if (string.IsNullOrEmpty(svgContent))
            {
                throw new InvalidOperationException("SVG renderer produced no output.");
            }

            var outputPath = args.Length > 4 ? args[4] : "metro_map.svg";
            await File.WriteAllTextAsync(outputPath, svgContent);
            
            stopwatch.Stop();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\nMetro map has been generated successfully: {outputPath}");
            Console.WriteLine($"Total processing time: {stopwatch.Elapsed.TotalSeconds:F2} seconds");
            Console.ResetColor();
        }

        private static void LogProcessingStep(string step, int nodeCount, int edgeCount, TimeSpan elapsed)
        {
            Console.WriteLine($"[{elapsed.TotalSeconds:F2}s] {step} completed:");
            Console.WriteLine($"  - Nodes processed: {nodeCount:N0}");
            Console.WriteLine($"  - Edges processed: {edgeCount:N0}");
        }

        private static bool ValidateInputFiles(string[] args)
        {
            if (args == null || args.Length < 4)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine("Usage: MetroMapGenerator <routes.txt> <trips.txt> <stops.txt> <stop_times.txt> [output.svg]");
                Console.ResetColor();
                return false;
            }

            var requiredFiles = new[] 
            { 
                ("routes.txt", args[0]), 
                ("trips.txt", args[1]), 
                ("stops.txt", args[2]), 
                ("stop_times.txt", args[3])
            };

            var allFilesExist = true;
            foreach (var (fileName, path) in requiredFiles)
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"Required file not found: {fileName} at path: {path}");
                    Console.ResetColor();
                    allFilesExist = false;
                }
            }

            return allFilesExist;
        }

        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            // Configuration
            services.AddSingleton(new ProcessingOptions
            {
                ParallelProcesses = Environment.ProcessorCount,
                NodeClusteringDistance = 0.05,
                MinStopDistance = 0.5,
                AngleSnap = 45,
                DenseAreaThreshold = 0.1,
                ForceDirectedIterations = 100
            });

            services.AddSingleton(new RenderingOptions
            {
                Width = 1200,
                Height = 800,
                Padding = 50,
                StationRadius = 4,
                LineWidth = 3,
                LabelFontSize = 12,
                BackgroundColor = "#ffffff",
                LineColor = "#000000",
                StationColor = "#ffffff",
                StationStrokeColor = "#000000",
                LabelColor = "#000000"
            });

            services.AddSingleton(new GeometryOptions
            {
                InitialTemperature = 1.0,
                CoolingFactor = 0.95,
                StopCriterion = 0.01,
                BendPenalty = 1.0,
                OverlapPenalty = 2.0
            });

            // Services
            services.AddScoped<GTFSProcessor>();
            services.AddScoped<TopologyOptimizer>();

            // Enregistrement avec injection des dépendances
            services.AddScoped<IGraphBuilder>(sp => 
                new GraphBuilder(sp.GetRequiredService<ProcessingOptions>()));
                
            services.AddScoped<IGraphOptimizer>(sp => 
                new GraphOptimizer(sp.GetRequiredService<ProcessingOptions>()));
                
            services.AddScoped<ISVGRenderer, SVGRenderer>();

            return services.BuildServiceProvider();
        }
    }
}