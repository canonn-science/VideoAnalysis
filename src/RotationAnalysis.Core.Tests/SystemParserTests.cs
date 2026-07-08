using RotationAnalysis.Core.Domain;
using RotationAnalysis.Core.Spansh.Models;
using Xunit;

namespace RotationAnalysis.Core.Tests;

public class SystemParserTests
{
    private static SpanshDumpResponse MakeDump(params SpanshBody[] bodies)
        => new()
        {
            System = new SpanshSystem
            {
                Id64 = 12345,
                Name = "Test System",
                Coords = new SpanshCoords { X = 1.0, Y = 2.0, Z = 3.0 },
                Bodies = bodies.ToList(),
            },
        };

    [Fact]
    public void ExtractRings_ComputesEstimatedPeriod_FromStarSolarMass()
    {
        var star = new SpanshBody
        {
            Name = "Test System A",
            Type = "Star",
            SolarMasses = 1.0,
            Rings = new List<SpanshRingOrBelt>
            {
                new() { Name = "Test System A A Ring", Type = "Metal Rich", InnerRadius = 100_000, OuterRadius = 180_000 },
            },
        };

        var rings = SystemParser.ExtractRings(MakeDump(star));

        var ring = Assert.Single(rings);
        Assert.False(ring.IsBelt);
        Assert.Equal("Test System A A Ring", ring.RingName);
        Assert.NotNull(ring.EstimatedPeriodSeconds);

        double expectedNominalRadius = RingMath.NominalRadiusMeters(100_000, 180_000);
        double expectedPeriod = RingMath.KeplerPeriodSeconds(expectedNominalRadius, 1.0 * RingMath.SolarMassKg);
        Assert.Equal(expectedPeriod, ring.EstimatedPeriodSeconds!.Value, precision: 3);
    }

    [Fact]
    public void ExtractRings_ComputesEstimatedPeriod_FromPlanetEarthMass()
    {
        var planet = new SpanshBody
        {
            Name = "Test System A 1",
            Type = "Planet",
            EarthMasses = 2.0,
            Belts = new List<SpanshRingOrBelt>
            {
                new() { Name = "Test System A 1 Belt", Type = "Icy", InnerRadius = 50_000, OuterRadius = 90_000 },
            },
        };

        var rings = SystemParser.ExtractRings(MakeDump(planet));

        var belt = Assert.Single(rings);
        Assert.True(belt.IsBelt);

        double expectedNominalRadius = RingMath.NominalRadiusMeters(50_000, 90_000);
        double expectedPeriod = RingMath.KeplerPeriodSeconds(expectedNominalRadius, 2.0 * RingMath.EarthMassKg);
        Assert.Equal(expectedPeriod, belt.EstimatedPeriodSeconds!.Value, precision: 3);
    }

    [Fact]
    public void ExtractRings_LeavesEstimatedPeriodNull_WhenParentMassIsUnknown()
    {
        var body = new SpanshBody
        {
            Name = "Test System A 2",
            Type = "Planet",
            // no SolarMasses or EarthMasses
            Rings = new List<SpanshRingOrBelt>
            {
                new() { Name = "Test System A 2 Ring", Type = "Rocky", InnerRadius = 10_000, OuterRadius = 20_000 },
            },
        };

        var rings = SystemParser.ExtractRings(MakeDump(body));

        var ring = Assert.Single(rings);
        Assert.Null(ring.EstimatedPeriodSeconds);
    }

    [Fact]
    public void ExtractRings_PopulatesBodyAndRingMetadata_FromStar()
    {
        var star = new SpanshBody
        {
            Name = "Test System A",
            Type = "Star",
            SubType = "K (Yellow-Orange) Star",
            SolarMasses = 1.0,
            SolarRadius = 1.0,
            Rings = new List<SpanshRingOrBelt>
            {
                new() { Name = "Test System A A Ring", Type = "Metal Rich", InnerRadius = 100_000, OuterRadius = 180_000, Mass = 4.2e18 },
            },
        };

        var ring = Assert.Single(SystemParser.ExtractRings(MakeDump(star)));

        Assert.Equal("K (Yellow-Orange) Star", ring.BodyType);
        Assert.Equal("Metal Rich", ring.MaterialType);
        Assert.Equal(4.2e18, ring.RingMassKg);
        Assert.Equal(1.0 * (RingMath.SolarMassKg / RingMath.EarthMassKg), ring.BodyMassEarthMasses!.Value, precision: 3);
        Assert.Equal(RingMath.SolarRadiusKm, ring.BodyRadiusKm!.Value, precision: 3);
    }

    [Fact]
    public void ExtractRings_PopulatesBodyMetadata_FromPlanetWithoutSubType()
    {
        var planet = new SpanshBody
        {
            Name = "Test System A 1",
            Type = "Planet",
            EarthMasses = 2.0,
            Radius = 6378.0, // km
            Belts = new List<SpanshRingOrBelt>
            {
                new() { Name = "Test System A 1 Belt", Type = "Icy", InnerRadius = 50_000, OuterRadius = 90_000 },
            },
        };

        var belt = Assert.Single(SystemParser.ExtractRings(MakeDump(planet)));

        Assert.Equal("Planet", belt.BodyType);
        Assert.Equal(2.0, belt.BodyMassEarthMasses);
        Assert.Equal(6378.0, belt.BodyRadiusKm!.Value, precision: 3);
        Assert.Null(belt.RingMassKg);
    }

    [Fact]
    public void ExtractRings_SkipsBodiesWithNoRingsOrBelts()
    {
        var barren = new SpanshBody { Name = "Test System A 3", Type = "Planet", EarthMasses = 1.0 };

        var rings = SystemParser.ExtractRings(MakeDump(barren));

        Assert.Empty(rings);
    }

    [Fact]
    public void ExtractRings_FlattensRingsAndBeltsAcrossMultipleBodies()
    {
        var star = new SpanshBody
        {
            Name = "Test System A",
            Type = "Star",
            SolarMasses = 1.0,
            Rings = new List<SpanshRingOrBelt>
            {
                new() { Name = "Ring 1", Type = "Metal Rich", InnerRadius = 100_000, OuterRadius = 180_000 },
                new() { Name = "Ring 2", Type = "Icy", InnerRadius = 200_000, OuterRadius = 260_000 },
            },
        };
        var planet = new SpanshBody
        {
            Name = "Test System A 1",
            Type = "Planet",
            EarthMasses = 1.0,
            Belts = new List<SpanshRingOrBelt>
            {
                new() { Name = "Belt 1", Type = "Rocky", InnerRadius = 10_000, OuterRadius = 20_000 },
            },
        };

        var rings = SystemParser.ExtractRings(MakeDump(star, planet));

        Assert.Equal(3, rings.Count);
        Assert.All(rings, r =>
        {
            Assert.Equal("Test System", r.SystemName);
            Assert.Equal(12345, r.SystemId64);
        });
    }
}
