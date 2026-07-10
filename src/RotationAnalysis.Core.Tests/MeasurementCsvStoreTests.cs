using RotationAnalysis.Core.Storage;
using Xunit;

namespace RotationAnalysis.Core.Tests;

public class MeasurementCsvStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MeasurementCsvStoreTests_" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string CsvPath => Path.Combine(_directory, "measurements.csv");

    [Fact]
    public void Constructor_MigratesOldHeader_PreservingExistingDataAndAddingEmptyNewColumns()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(CsvPath,
            "Timestamp,System Name,id64,x,y,z,Body Name,Ring Name,innerRadius,outerRadius,Width,estimated rotation,observed rotation,video filename,submitted\r\n" +
            "2026-01-01T00:00:00Z,Test System,12345,1,2,3,Test Body,Test Ring,100000,180000,80000,1000,1050,test.mp4,True\r\n");

        var store = new MeasurementCsvStore(CsvPath);

        var records = store.ReadAll();
        var record = Assert.Single(records);
        Assert.Equal("Test System", record.SystemName);
        Assert.Equal("Test Body", record.BodyName);
        Assert.Equal("Test Ring", record.RingName);
        Assert.Equal(100000, record.InnerRadius);
        Assert.True(record.Submitted);
        Assert.Equal(string.Empty, record.BodyType);
        Assert.Null(record.BodyMassEarthMasses);
        Assert.Equal(string.Empty, record.RingType);
        Assert.Null(record.RingMassKg);

        var headerLine = File.ReadLines(CsvPath).First();
        Assert.Contains("Body Type", headerLine);
        Assert.Contains("Body Mass", headerLine);
        Assert.Contains("Ring Type", headerLine);
        Assert.Contains("Ring Mass", headerLine);
    }

    [Fact]
    public void Constructor_LeavesNewFormatFileUntouched()
    {
        Directory.CreateDirectory(_directory);
        var original =
            "Timestamp,System Name,id64,x,y,z,Body Name,Body Type,Body Mass,Ring Name,Ring Type,Ring Mass,innerRadius,outerRadius,Width,estimated rotation,observed rotation,video filename,submitted\r\n" +
            "2026-01-01T00:00:00Z,Test System,12345,1,2,3,Test Body,Icy body,1.5,Test Ring,Icy,2.5,100000,180000,80000,1000,1050,test.mp4,True\r\n";
        File.WriteAllText(CsvPath, original);

        var store = new MeasurementCsvStore(CsvPath);
        var record = Assert.Single(store.ReadAll());

        Assert.Equal("Icy body", record.BodyType);
        Assert.Equal(1.5, record.BodyMassEarthMasses);
        Assert.Equal("Icy", record.RingType);
        Assert.Equal(2.5, record.RingMassKg);
    }

    [Fact]
    public void Constructor_DoesNothingWhenFileDoesNotExist()
    {
        var store = new MeasurementCsvStore(CsvPath);
        Assert.False(File.Exists(CsvPath));
        Assert.Empty(store.ReadAll());
    }

    [Fact]
    public void AppendThenReadAll_RoundTripsFullRotationColumns()
    {
        var store = new MeasurementCsvStore(CsvPath);
        store.Append(new MeasurementRecord
        {
            Timestamp = DateTime.UtcNow,
            SystemName = "Test System",
            BodyName = "Test Body",
            RingName = "Test Ring",
            InnerRadius = 100000,
            OuterRadius = 180000,
            Width = 80000,
            EstimatedRotationSeconds = 1000,
            ObservedRotationSeconds = 1050,
            VideoFilename = "test.mp4",
            MeasuredPeriodSeconds = 1042.5,
            MeasuredPeriodErrSeconds = 0.8,
            NReferenceSamples = 4,
            RateVsMeasuredPctDiff = 0.72,
        });

        var record = Assert.Single(store.ReadAll());
        Assert.Equal(1042.5, record.MeasuredPeriodSeconds);
        Assert.Equal(0.8, record.MeasuredPeriodErrSeconds);
        Assert.Equal(4, record.NReferenceSamples);
        Assert.Equal(0.72, record.RateVsMeasuredPctDiff);
    }

    [Fact]
    public void ReadAll_TreatsMissingFullRotationColumns_AsNull()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(CsvPath,
            "Timestamp,System Name,id64,x,y,z,Body Name,Body Type,Body Mass,Ring Name,Ring Type,Ring Mass,innerRadius,outerRadius,Width,estimated rotation,observed rotation,video filename,submitted\r\n" +
            "2026-01-01T00:00:00Z,Test System,12345,1,2,3,Test Body,,,Test Ring,,,100000,180000,80000,1000,1050,test.mp4,True\r\n");

        var store = new MeasurementCsvStore(CsvPath);
        var record = Assert.Single(store.ReadAll());

        Assert.Null(record.MeasuredPeriodSeconds);
        Assert.Null(record.MeasuredPeriodErrSeconds);
        Assert.Null(record.NReferenceSamples);
        Assert.Null(record.RateVsMeasuredPctDiff);
    }
}
