using System.Text.Json;
using AttendancePro.Web.Models;

namespace AttendancePro.Web.Services;

public record GeoResult(bool Inside, double DistanceMeters);

/// <summary>Server-side authoritative geofence maths (the app never decides on its own).</summary>
public static class GeoService
{
    private const double EarthRadius = 6_371_000;

    public static double Haversine(double lat1, double lng1, double lat2, double lng2)
    {
        double dLat = ToRad(lat2 - lat1), dLng = ToRad(lng2 - lng1);
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * EarthRadius * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    public static List<(double Lat, double Lng)> ParsePolygon(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            var arr = JsonSerializer.Deserialize<double[][]>(json) ?? Array.Empty<double[]>();
            return arr.Where(p => p.Length >= 2).Select(p => (p[0], p[1])).ToList();
        }
        catch { return new(); }
    }

    /// <summary>
    /// Circle: distance is metres from the boundary (negative = inside). Polygon: distance to nearest edge,
    /// reported as the same signed value so the app can say "42 m away".
    /// </summary>
    public static GeoResult Evaluate(Site site, double lat, double lng, double toleranceMeters = 0)
    {
        if (site.Shape == SiteShape.Circle)
        {
            var d = Haversine(site.CenterLat, site.CenterLng, lat, lng);
            var outside = d - site.RadiusMeters;
            return new GeoResult(outside <= toleranceMeters, outside);
        }

        var poly = ParsePolygon(site.PolygonJson);
        if (poly.Count < 3) return new GeoResult(false, double.MaxValue);
        bool inside = PointInPolygon(poly, lat, lng);
        double edge = DistanceToPolygonEdge(poly, lat, lng);
        double signed = inside ? -edge : edge;
        return new GeoResult(inside || edge <= toleranceMeters, signed);
    }

    public static bool PointInPolygon(List<(double Lat, double Lng)> poly, double lat, double lng)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var (yi, xi) = poly[i]; var (yj, xj) = poly[j];
            bool cross = (yi > lat) != (yj > lat) && lng < (xj - xi) * (lat - yi) / (yj - yi) + xi;
            if (cross) inside = !inside;
        }
        return inside;
    }

    private static double DistanceToPolygonEdge(List<(double Lat, double Lng)> poly, double lat, double lng)
    {
        double best = double.MaxValue;
        // Local equirectangular projection (accurate for campus-scale polygons).
        double cosLat = Math.Cos(ToRad(lat));
        (double x, double y) P(double la, double ln) => (ToRad(ln - lng) * cosLat * EarthRadius, ToRad(la - lat) * EarthRadius);
        for (int i = 0; i < poly.Count; i++)
        {
            var a = P(poly[i].Lat, poly[i].Lng);
            var b = P(poly[(i + 1) % poly.Count].Lat, poly[(i + 1) % poly.Count].Lng);
            double dx = b.x - a.x, dy = b.y - a.y;
            double len2 = dx * dx + dy * dy;
            double t = len2 == 0 ? 0 : Math.Clamp(-(a.x * dx + a.y * dy) / len2, 0, 1);
            double px = a.x + t * dx, py = a.y + t * dy;
            best = Math.Min(best, Math.Sqrt(px * px + py * py));
        }
        return best;
    }

    private static double ToRad(double deg) => deg * Math.PI / 180;
}
