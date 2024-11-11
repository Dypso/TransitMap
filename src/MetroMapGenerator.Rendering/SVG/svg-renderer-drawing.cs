using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using MetroMapGenerator.Core.Models;
using MetroMapGenerator.Core.Configuration;
using MetroMapGenerator.Rendering.Models;

namespace MetroMapGenerator.Rendering.SVG
{
    public partial class SVGRenderer
    {
        private void WriteHeader(RenderingOptions options)
        {
            _builder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            _builder.AppendLine($"<svg width=\"{options.Width}\" height=\"{options.Height}\" " +
                              $"viewBox=\"0 0 {options.Width} {options.Height}\" " +
                              $"version=\"1.1\" xmlns=\"http://www.w3.org/2000/svg\" " +
                              $"xmlns:xlink=\"http://www.w3.org/1999/xlink\">");

            _builder.AppendLine($"  <rect width=\"100%\" height=\"100%\" fill=\"{options.BackgroundColor}\"/>");
        }

        private void WriteDefinitions(RenderingOptions options)
        {
            _builder.AppendLine("  <defs>");
            WriteLineFilter();
            WriteStationFilter();
            WriteStationGradients(options);
            WriteTransferPatterns(options);
            WriteTerminalMarkers(options);
            _builder.AppendLine("  </defs>");
        }

        private void WriteLineFilter()
        {
            _builder.AppendLine(@"    <filter id=""line-shadow"" x=""-20%"" y=""-20%"" width=""140%"" height=""140%"">
      <feGaussianBlur in=""SourceAlpha"" stdDeviation=""2""/>
      <feOffset dx=""1"" dy=""1"" result=""offsetblur""/>
      <feComponentTransfer>
        <feFuncA type=""linear"" slope=""0.3""/>
      </feComponentTransfer>
      <feMerge>
        <feMergeNode/>
        <feMergeNode in=""SourceGraphic""/>
      </feMerge>
    </filter>");
        }

        private void WriteStationFilter()
        {
            _builder.AppendLine(@"    <filter id=""station-shadow"" x=""-50%"" y=""-50%"" width=""200%"" height=""200%"">
      <feGaussianBlur in=""SourceAlpha"" stdDeviation=""1""/>
      <feOffset dx=""0.5"" dy=""0.5"" result=""offsetblur""/>
      <feComponentTransfer>
        <feFuncA type=""linear"" slope=""0.5""/>
      </feComponentTransfer>
      <feMerge>
        <feMergeNode/>
        <feMergeNode in=""SourceGraphic""/>
      </feMerge>
    </filter>");
        }

        private void WriteStationGradients(RenderingOptions options)
        {
            _builder.AppendLine($@"    <radialGradient id=""station-gradient"" cx=""0.3"" cy=""0.3"" r=""0.7"">
      <stop offset=""0%"" style=""stop-color:{options.StationColor};stop-opacity:1""/>
      <stop offset=""100%"" style=""stop-color:#f0f0f0;stop-opacity:1""/>
    </radialGradient>");

            _builder.AppendLine($@"    <radialGradient id=""transfer-gradient"" cx=""0.3"" cy=""0.3"" r=""0.7"">
      <stop offset=""0%"" style=""stop-color:{options.StationColor};stop-opacity:1""/>
      <stop offset=""100%"" style=""stop-color:#e0e0e0;stop-opacity:1""/>
    </radialGradient>");
        }

        private void WriteTransferPatterns(RenderingOptions options)
        {
            _builder.AppendLine($@"    <pattern id=""transfer-pattern"" width=""8"" height=""8"" patternUnits=""userSpaceOnUse"">
      <circle cx=""4"" cy=""4"" r=""1.5"" fill=""{options.StationColor}""/>
    </pattern>");
        }

        private void WriteTerminalMarkers(RenderingOptions options)
        {
            _builder.AppendLine($@"    <marker id=""terminal"" viewBox=""0 0 10 10"" 
                                    refX=""5"" refY=""5"" markerWidth=""3"" markerHeight=""3"">
      <circle cx=""5"" cy=""5"" r=""4"" 
              fill=""{options.StationColor}"" 
              stroke=""{options.StationStrokeColor}"" 
              stroke-width=""1""/>
    </marker>");
        }

        private void WriteStyles(RenderingOptions options)
        {
            _builder.AppendLine($@"  <style>
    .metro-line {{
      fill: none;
      stroke-linecap: round;
      stroke-linejoin: round;
      filter: url(#line-shadow);
    }}
    .line-main {{
      stroke-width: {STROKE_WIDTH_MAIN};
    }}
    .line-secondary {{
      stroke-width: {STROKE_WIDTH_SECONDARY};
    }}
    .station {{
      filter: url(#station-shadow);
    }}
    .station-regular {{
      fill: url(#station-gradient);
      stroke: {options.StationStrokeColor};
      stroke-width: 1.5;
    }}
    .station-transfer {{
      fill: url(#transfer-gradient);
      stroke: {options.StationStrokeColor};
      stroke-width: 2;
    }}
    .station-label {{
      font-family: {FONT_FAMILY};
      font-size: {options.LabelFontSize}px;
      fill: {options.LabelColor};
      dominant-baseline: central;
    }}
    .line-label {{
      font-family: {FONT_FAMILY};
      font-weight: bold;
      font-size: {options.LabelFontSize * 1.2}px;
      fill: white;
      text-anchor: middle;
      dominant-baseline: central;
    }}
  </style>");
        }

        private async Task WriteMetroLine(MetroLine line, RenderingOptions options)
        {
            try
            {
                await Task.Run(() => {
                    _builder.AppendLine($"  <g class=\"metro-line-group\" id=\"line-{HttpUtility.HtmlEncode(line.Id)}\">");

                    WritePath(
                        line.Segments,
                        line.Color,
                        line.Importance > 0.5f ? "line-main" : "line-secondary",
                        line.Id
                    );

                    if (HasParallelLines(line))
                    {
                        WriteParallelPaths(line, options);
                    }

                    WriteStops(line, options);

                    _builder.AppendLine("  </g>");
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error writing metro line {line.Id}: {ex.Message}");
                throw;
            }
        }

        private void WritePath(List<LineSegment> segments, string color, string className, string lineId)
        {
            var pathBuilder = new StringBuilder();
            pathBuilder.Append("    M ");

            for (int i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                
                if (i == 0)
                {
                    pathBuilder.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "{0:F1},{1:F1} ",
                        segment.Start.X,
                        segment.Start.Y
                    );
                }

                if (segment.IsTransfer && segment.ControlPoints.Count >= 2)
                {
                    pathBuilder.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "C {0:F1},{1:F1} {2:F1},{3:F1} {4:F1},{5:F1} ",
                        segment.ControlPoints[0].X, segment.ControlPoints[0].Y,
                        segment.ControlPoints[1].X, segment.ControlPoints[1].Y,
                        segment.End.X, segment.End.Y
                    );
                }
                else
                {
                    pathBuilder.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "L {0:F1},{1:F1} ",
                        segment.End.X,
                        segment.End.Y
                    );
                }
            }

            var encodedId = HttpUtility.HtmlEncode(lineId);
            _builder.AppendLine($"    <path class=\"metro-line {className}\" " +
                              $"d=\"{pathBuilder}\" " +
                              $"stroke=\"{color}\" " +
                              $"id=\"path-{encodedId}\"/>");
        }

        private void WriteParallelPaths(MetroLine line, RenderingOptions options)
        {
            var parallelLines = GetParallelLines(line);
            var index = 0;
            foreach (var parallelLine in parallelLines)
            {
                index++;
                var offset = PARALLEL_LINE_OFFSET * index;
                var offsetSegments = OffsetLineSegments(line.Segments, offset);
                WritePath(
                    offsetSegments,
                    GetLineColor(parallelLine),
                    "line-secondary",
                    $"{line.Id}-parallel-{index}"
                );
            }
        }

        private void WriteStops(MetroLine line, RenderingOptions options)
        {
            foreach (var station in line.Stations)
            {
                if (!_stationInfo.TryGetValue(station.Id, out var info))
                    continue;

                if (info.ConnectedLines.Count > 1)
                {
                    WriteTransferStation(info, options);
                }
                else
                {
                    WriteRegularStation(info, options);
                }
            }
        }

        private async Task WriteStation(StationInfo station, RenderingOptions options)
        {
            await Task.Run(() => {
                var radius = station.ConnectedLines.Count > 1 ? 
                    STATION_RADIUS_TRANSFER : STATION_RADIUS_REGULAR;

                var className = station.ConnectedLines.Count > 1 ? 
                    "station-transfer" : "station-regular";

                _builder.AppendLine($"    <circle class=\"station {className}\" " +
                                  $"cx=\"{station.Position.X:F1}\" " +
                                  $"cy=\"{station.Position.Y:F1}\" " +
                                  $"r=\"{radius}\" " +
                                  $"id=\"station-{station.Node.Id}\"/>");
            });
        }

        private async Task WriteStationLabel(StationInfo station, RenderingOptions options)
        {
            await Task.Run(() => {
                var stationName = HttpUtility.HtmlEncode(station.Node.Name);
                _builder.AppendLine($"    <text class=\"station-label\" " +
                                  $"x=\"{station.LabelPosition.X:F1}\" " +
                                  $"y=\"{station.LabelPosition.Y:F1}\" " +
                                  $"transform=\"rotate({station.LabelAngle:F1} " +
                                  $"{station.LabelPosition.X:F1} " +
                                  $"{station.LabelPosition.Y:F1})\">" +
                                  $"{stationName}</text>");
            });
        }

        private void WriteTransferStation(StationInfo info, RenderingOptions options)
        {
            _builder.AppendLine($"    <circle class=\"station station-transfer\" " +
                              $"cx=\"{info.Position.X:F1}\" " +
                              $"cy=\"{info.Position.Y:F1}\" " +
                              $"r=\"{STATION_RADIUS_TRANSFER}\"/>");

            var angleStep = 360f / info.ConnectedLines.Count;
            var currentAngle = 0f;

            foreach (var lineId in info.ConnectedLines)
            {
                var offset = new Vector2(
                    MathF.Cos(currentAngle * MathF.PI / 180f) * STATION_RADIUS_TRANSFER * 0.7f,
                    MathF.Sin(currentAngle * MathF.PI / 180f) * STATION_RADIUS_TRANSFER * 0.7f
                );

                var connectorPos = info.Position + offset;
                _builder.AppendLine($"    <circle class=\"station-connector\" " +
                                  $"cx=\"{connectorPos.X:F1}\" " +
                                  $"cy=\"{connectorPos.Y:F1}\" " +
                                  $"r=\"2\" fill=\"{GetLineColor(lineId)}\"/>");

                currentAngle += angleStep;
            }
        }

        private void WriteRegularStation(StationInfo info, RenderingOptions options)
        {
            _builder.AppendLine($"    <circle class=\"station station-regular\" " +
                              $"cx=\"{info.Position.X:F1}\" " +
                              $"cy=\"{info.Position.Y:F1}\" " +
                              $"r=\"{STATION_RADIUS_REGULAR}\"/>");
        }

        private void WriteFooter()
        {
            _builder.AppendLine("</svg>");
        }
    }
}