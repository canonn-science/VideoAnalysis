using RotationAnalysis.Core.Storage;
using Xunit;

namespace RotationAnalysis.Core.Tests;

public class JetLengthCsvStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "JetLengthCsvStoreTests_" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string CsvPath => Path.Combine(_directory, "jetlength.csv");

    [Fact]
    public void Append_ThenReadAll_RoundTripsRecord()
    {
        var store = new JetLengthCsvStore(CsvPath);
        store.Append(new JetLengthRecord
        {
            SystemName = "Eoch Flya NN-I d10-328",
            BodyName = "Eoch Flya NN-I d10-328 A",
            Distance = 1.01,
            AbsoluteMagnitude = 5.241913,
            BodyId = 1,
            Luminosity = "VII",
            MainStar = true,
            SpectralClass = "N0",
            SurfaceTemperature = 9859543.0,
            UpdateTime = "2026-07-03 23:34:56+00",
        });

        var record = Assert.Single(store.ReadAll());
        Assert.Equal("Eoch Flya NN-I d10-328", record.SystemName);
        Assert.Equal("Eoch Flya NN-I d10-328 A", record.BodyName);
        Assert.Equal(1.01, record.Distance);
        Assert.Equal(1, record.BodyId);
        Assert.True(record.MainStar);
        Assert.Equal("N0", record.SpectralClass);
    }

    [Fact]
    public void ReadAll_ReturnsEmptyList_WhenFileDoesNotExist()
    {
        var store = new JetLengthCsvStore(CsvPath);
        Assert.Empty(store.ReadAll());
    }

    [Fact]
    public void WrittenHeader_MatchesNeutronJetColumnNames()
    {
        var store = new JetLengthCsvStore(CsvPath);
        store.Append(new JetLengthRecord { SystemName = "S", BodyName = "B", Distance = 1.0 });

        var headerLine = File.ReadLines(CsvPath).First();
        Assert.Equal(
            "systemName,bodyName,distance,absoluteMagnitude,age,argOfPeriapsis,ascendingNode,axialTilt,bodyId,distanceToArrival,luminosity,mainStar,meanAnomaly,orbitalEccentricity,orbitalInclination,orbitalPeriod,rotationalPeriod,semiMajorAxis,solarMasses,solarRadius,spectralClass,surfaceTemperature,updateTime",
            headerLine);
    }
}
