using RotationAnalysis.Core.Spansh.Models;

namespace RotationAnalysis.Core.Domain;

public static class JetTargetParser
{
    /// <summary>Walks every body in the dump and keeps only neutron stars and white dwarfs.
    /// White dwarfs carry a sub-classification in Spansh's subType (e.g. "White Dwarf (DA)
    /// Star"), confirmed against a real dump (Sirius B), so this matches by prefix rather than
    /// exact equality - an exact match against the bare string "White Dwarf" would silently
    /// exclude every real white dwarf.</summary>
    public static List<JetTargetInfo> ExtractTargets(SpanshDumpResponse dump)
    {
        var system = dump.System;
        var results = new List<JetTargetInfo>();

        foreach (var body in system.Bodies)
        {
            var bodyType = body.SubType ?? body.Type;
            if (!IsNeutronStarOrWhiteDwarf(bodyType))
            {
                continue;
            }

            results.Add(new JetTargetInfo
            {
                SystemName = system.Name,
                SystemId64 = system.Id64,
                SystemX = system.Coords.X,
                SystemY = system.Coords.Y,
                SystemZ = system.Coords.Z,
                BodyName = body.Name,
                BodyType = bodyType,
                AbsoluteMagnitude = body.AbsoluteMagnitude,
                Age = body.Age,
                ArgOfPeriapsis = body.ArgOfPeriapsis,
                AscendingNode = body.AscendingNode,
                AxialTilt = body.AxialTilt,
                BodyId = body.BodyId,
                DistanceToArrival = body.DistanceToArrival,
                Luminosity = body.Luminosity,
                MainStar = body.MainStar,
                MeanAnomaly = body.MeanAnomaly,
                OrbitalEccentricity = body.OrbitalEccentricity,
                OrbitalInclination = body.OrbitalInclination,
                OrbitalPeriod = body.OrbitalPeriod,
                RotationalPeriod = body.RotationalPeriod,
                SemiMajorAxis = body.SemiMajorAxis,
                SolarMasses = body.SolarMasses,
                SolarRadius = body.SolarRadius,
                SpectralClass = body.SpectralClass,
                SurfaceTemperature = body.SurfaceTemperature,
                UpdateTime = body.UpdateTime,
            });
        }

        return results;
    }

    private static bool IsNeutronStarOrWhiteDwarf(string? bodyType) =>
        bodyType is not null &&
        (bodyType.StartsWith("Neutron Star", StringComparison.OrdinalIgnoreCase) ||
         bodyType.StartsWith("White Dwarf", StringComparison.OrdinalIgnoreCase));
}
