namespace WebApi.Messaging;

internal static class GeoDistance
{
    private const double EarthRadiusMeters = 6_371_000d;

    public static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var sinDLat = Math.Sin(dLat / 2);
        var sinDLon = Math.Sin(dLon / 2);
        var a = (sinDLat * sinDLat)
              + (Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) * sinDLon * sinDLon);
        var c = 2 * Math.Asin(Math.Min(1, Math.Sqrt(a)));
        return EarthRadiusMeters * c;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180d;
}
