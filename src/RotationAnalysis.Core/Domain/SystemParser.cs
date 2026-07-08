using RotationAnalysis.Core.Spansh.Models;

namespace RotationAnalysis.Core.Domain;

public static class SystemParser
{
    /// <summary>Walks every body in the dump and collects all rings and belts into a flat list.</summary>
    public static List<RingInfo> ExtractRings(SpanshDumpResponse dump)
    {
        var system = dump.System;
        var results = new List<RingInfo>();

        foreach (var body in system.Bodies)
        {
            double? parentMassKg = ResolveParentMassKg(body);

            AppendEntries(results, system, body, body.Rings, isBelt: false, parentMassKg);
            AppendEntries(results, system, body, body.Belts, isBelt: true, parentMassKg);
        }

        return results;
    }

    private static double? ResolveParentMassKg(SpanshBody body)
    {
        if (body.SolarMasses is double solar)
        {
            return solar * RingMath.SolarMassKg;
        }
        if (body.EarthMasses is double earth)
        {
            return earth * RingMath.EarthMassKg;
        }
        return null;
    }

    private static double? ResolveBodyMassEarthMasses(SpanshBody body)
    {
        if (body.SolarMasses is double solar)
        {
            return solar * (RingMath.SolarMassKg / RingMath.EarthMassKg);
        }
        if (body.EarthMasses is double earth)
        {
            return earth;
        }
        return null;
    }

    private static double? ResolveBodyRadiusKm(SpanshBody body)
    {
        if (body.Radius is double radiusKm)
        {
            return radiusKm;
        }
        if (body.SolarRadius is double solarRadius)
        {
            return solarRadius * RingMath.SolarRadiusKm;
        }
        return null;
    }

    private static void AppendEntries(
        List<RingInfo> results,
        SpanshSystem system,
        SpanshBody body,
        List<SpanshRingOrBelt>? entries,
        bool isBelt,
        double? parentMassKg)
    {
        if (entries is null)
        {
            return;
        }

        foreach (var entry in entries)
        {
            double? estimatedPeriod = parentMassKg is double mass
                ? RingMath.KeplerPeriodSeconds(RingMath.NominalRadiusMeters(entry.InnerRadius, entry.OuterRadius), mass)
                : null;

            results.Add(new RingInfo
            {
                SystemName = system.Name,
                SystemId64 = system.Id64,
                SystemX = system.Coords.X,
                SystemY = system.Coords.Y,
                SystemZ = system.Coords.Z,
                BodyName = body.Name,
                RingName = entry.Name,
                IsBelt = isBelt,
                MaterialType = entry.Type,
                BodyType = body.SubType ?? body.Type,
                BodyMassEarthMasses = ResolveBodyMassEarthMasses(body),
                BodyRadiusKm = ResolveBodyRadiusKm(body),
                RingMassKg = entry.Mass,
                InnerRadiusMeters = entry.InnerRadius,
                OuterRadiusMeters = entry.OuterRadius,
                EstimatedPeriodSeconds = estimatedPeriod,
            });
        }
    }
}
