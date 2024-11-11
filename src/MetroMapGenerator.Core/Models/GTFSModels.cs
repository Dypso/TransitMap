using CsvHelper.Configuration;

namespace MetroMapGenerator.Core.Models
{
    public class RouteMap : ClassMap<Route>
    {
        public RouteMap()
        {
            Map(m => m.RouteId).Name("route_id").Index(0);
            Map(m => m.AgencyId).Name("agency_id").Index(1);
            Map(m => m.ShortName).Name("route_short_name").Index(2);
            Map(m => m.LongName).Name("route_long_name").Index(3);
            Map(m => m.RouteType).Name("route_type").Index(4);
            Map(m => m.RouteColor).Optional().Default(null);
            Map(m => m.RouteTextColor).Optional().Default(null);
            Map(m => m.RouteUrl).Optional().Default(null);
            Map(m => m.TicketingDeepLinkId).Optional().Default(null);
        }
    }

    public class StopMap : ClassMap<Stop>
    {
        public StopMap()
        {
            Map(m => m.StopId).Name("stop_id").Index(0);
            Map(m => m.Name).Name("stop_name").Index(1);
            Map(m => m.Latitude).Name("stop_lat").Index(3);
            Map(m => m.Longitude).Name("stop_lon").Index(4);
            Map(m => m.LocationType).Optional().Default(null);
            Map(m => m.ParentStation).Optional().Default(null);
            Map(m => m.Description).Optional().Default(null);
            Map(m => m.ZoneId).Optional().Default(null);
            Map(m => m.StopUrl).Optional().Default(null);
            Map(m => m.WheelchairBoarding).Optional().Default(null);
            Map(m => m.PlatformCode).Optional().Default(null);
            Map(m => m.StopCode).Optional().Default(null);
            Map(m => m.LevelId).Optional().Default(null);
            Map(m => m.SignpostedAs).Optional().Default(null);
        }
    }

    public class TripMap : ClassMap<Trip>
    {
        public TripMap()
        {
            Map(m => m.TripId).Name("trip_id").Index(2);
            Map(m => m.RouteId).Name("route_id").Index(0);
            Map(m => m.ServiceId).Name("service_id").Index(1);
            Map(m => m.DirectionId).Optional().Default("0");
            Map(m => m.ShapeId).Optional().Default(null);
            Map(m => m.TripHeadsign).Optional().Default(null);
            Map(m => m.TripShortName).Optional().Default(null);
            Map(m => m.BlockId).Optional().Default(null);
            Map(m => m.WheelchairAccessible).Optional().Default(null);
            Map(m => m.BikesAllowed).Optional().Default(null);
        }
    }

    public class StopTimeMap : ClassMap<StopTime>
    {
        public StopTimeMap()
        {
            Map(m => m.TripId).Name("trip_id").Index(0);
            Map(m => m.StopId).Name("stop_id").Index(3);
            Map(m => m.StopSequence).Name("stop_sequence").Index(4);
            Map(m => m.ArrivalTime).Name("arrival_time").Index(1);
            Map(m => m.DepartureTime).Name("departure_time").Index(2);
            Map(m => m.PickupType).Optional().Default(null);
            Map(m => m.DropOffType).Optional().Default(null);
            Map(m => m.ShapeDistTraveled).Optional().Default(null);
            Map(m => m.Timepoint).Optional().Default(null);
            Map(m => m.StopHeadsign).Optional().Default(null);
            Map(m => m.ContinuousPickup).Optional().Default(null);
            Map(m => m.ContinuousDropOff).Optional().Default(null);
        }
    }

    // Modèles de données inchangés...
    public record Route
    {
        public string RouteId { get; init; } = string.Empty;
        public string AgencyId { get; init; } = string.Empty;
        public string ShortName { get; init; } = string.Empty;
        public string LongName { get; init; } = string.Empty;
        public string RouteType { get; init; } = string.Empty;
        public string? RouteColor { get; init; }
        public string? RouteTextColor { get; init; }
        public string? RouteUrl { get; init; }
        public string? TicketingDeepLinkId { get; init; }
    }

    public record Stop
    {
        public string StopId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public double Latitude { get; init; }
        public double Longitude { get; init; }
        public string? LocationType { get; init; }
        public string? ParentStation { get; init; }
        public string? Description { get; init; }
        public string? ZoneId { get; init; }
        public string? StopUrl { get; init; }
        public string? WheelchairBoarding { get; init; }
        public string? PlatformCode { get; init; }
        public string? StopCode { get; init; }
        public string? LevelId { get; init; }
        public string? SignpostedAs { get; init; }
    }

    public record Trip
    {
        public string TripId { get; init; } = string.Empty;
        public string RouteId { get; init; } = string.Empty;
        public string ServiceId { get; init; } = string.Empty;
        public string DirectionId { get; init; } = "0";
        public string? ShapeId { get; init; }
        public string? TripHeadsign { get; init; }
        public string? TripShortName { get; init; }
        public string? BlockId { get; init; }
        public string? WheelchairAccessible { get; init; }
        public string? BikesAllowed { get; init; }
    }

    public record StopTime
    {
        public string TripId { get; init; } = string.Empty;
        public string StopId { get; init; } = string.Empty;
        public int StopSequence { get; init; }
        public string ArrivalTime { get; init; } = string.Empty;
        public string DepartureTime { get; init; } = string.Empty;
        public string? PickupType { get; init; }
        public string? DropOffType { get; init; }
        public string? ShapeDistTraveled { get; init; }
        public string? Timepoint { get; init; }
        public string? StopHeadsign { get; init; }
        public string? ContinuousPickup { get; init; }
        public string? ContinuousDropOff { get; init; }
    }
}