using RotationAnalysis.Core.Domain;
using RotationAnalysis.Core.Spansh.Models;
using Xunit;

namespace RotationAnalysis.Core.Tests;

public class JetTargetParserTests
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
    public void ExtractTargets_IncludesNeutronStar()
    {
        var body = new SpanshBody { Name = "Test System A", Type = "Star", SubType = "Neutron Star" };

        var result = Assert.Single(JetTargetParser.ExtractTargets(MakeDump(body)));

        Assert.True(result.IsNeutronStar);
        Assert.Equal("Neutron Star", result.BodyType);
    }

    [Theory]
    [InlineData("White Dwarf (DA) Star")]
    [InlineData("White Dwarf (DB) Star")]
    [InlineData("White Dwarf (DC) Star")]
    public void ExtractTargets_IncludesWhiteDwarf_RegardlessOfSubClassification(string subType)
    {
        var body = new SpanshBody { Name = "Test System B", Type = "Star", SubType = subType };

        var result = Assert.Single(JetTargetParser.ExtractTargets(MakeDump(body)));

        Assert.False(result.IsNeutronStar);
        Assert.Equal(subType, result.BodyType);
    }

    [Theory]
    [InlineData("K (Yellow-Orange) Star")]
    [InlineData("Planet")]
    [InlineData("M (Red dwarf) Star")]
    public void ExtractTargets_ExcludesOtherBodyTypes(string subType)
    {
        var body = new SpanshBody { Name = "Test System C", Type = "Star", SubType = subType };

        Assert.Empty(JetTargetParser.ExtractTargets(MakeDump(body)));
    }

    [Fact]
    public void ExtractTargets_CopiesAllBodyCsvFields()
    {
        var body = new SpanshBody
        {
            Name = "Test System A",
            Type = "Star",
            SubType = "Neutron Star",
            BodyId = 1,
            AbsoluteMagnitude = 5.24,
            Age = 12866,
            ArgOfPeriapsis = 290.49,
            AscendingNode = 169.91,
            AxialTilt = 0.0,
            DistanceToArrival = 0.0,
            Luminosity = "VII",
            MainStar = true,
            MeanAnomaly = 2.49,
            OrbitalEccentricity = 0.016,
            OrbitalInclination = 8.06,
            OrbitalPeriod = 25822.29,
            RotationalPeriod = 5.37e-05,
            SemiMajorAxis = 5.79,
            SolarMasses = 0.585938,
            SolarRadius = 1.727e-05,
            SpectralClass = "N0",
            SurfaceTemperature = 9859543.0,
            UpdateTime = "2026-07-03 23:34:56+00",
        };

        var result = Assert.Single(JetTargetParser.ExtractTargets(MakeDump(body)));

        Assert.Equal(1, result.BodyId);
        Assert.Equal(5.24, result.AbsoluteMagnitude);
        Assert.Equal("VII", result.Luminosity);
        Assert.True(result.MainStar);
        Assert.Equal("N0", result.SpectralClass);
        Assert.Equal(9859543.0, result.SurfaceTemperature);
        Assert.Equal("2026-07-03 23:34:56+00", result.UpdateTime);
    }
}
