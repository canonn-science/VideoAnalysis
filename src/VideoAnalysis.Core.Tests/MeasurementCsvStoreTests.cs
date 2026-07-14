using VideoAnalysis.Core.Storage;
using Xunit;

namespace VideoAnalysis.Core.Tests;

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

    private string CsvPath => Path.Combine(_directory, "ring_period.csv");
    private string LegacyCsvPath => Path.Combine(_directory, "measurements.csv");

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
        Assert.Null(record.BodyRadiusKm);
        Assert.Equal(string.Empty, record.RingType);
        Assert.Null(record.RingMassKg);

        var headerLine = File.ReadLines(CsvPath).First();
        Assert.Contains("Body Type", headerLine);
        Assert.Contains("Body Mass", headerLine);
        Assert.Contains("Body Radius", headerLine);
        Assert.Contains("Ring Type", headerLine);
        Assert.Contains("Ring Mass", headerLine);
    }

    [Fact]
    public void Constructor_MigratesHeaderMissingOnlyBodyRadius_PreservingExistingDataAndAddingEmptyColumn()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(CsvPath,
            "Timestamp,System Name,id64,x,y,z,Body Name,Body Type,Body Mass,Ring Name,Ring Type,Ring Mass,innerRadius,outerRadius,Width,estimated rotation,observed rotation,video filename,submitted\r\n" +
            "2026-01-01T00:00:00Z,Test System,12345,1,2,3,Test Body,Icy body,1.5,Test Ring,Icy,2.5,100000,180000,80000,1000,1050,test.mp4,True\r\n");

        var store = new MeasurementCsvStore(CsvPath);

        var record = Assert.Single(store.ReadAll());
        Assert.Equal("Icy body", record.BodyType);
        Assert.Equal(1.5, record.BodyMassEarthMasses);
        Assert.Null(record.BodyRadiusKm);
        Assert.Equal("Icy", record.RingType);

        var headerLine = File.ReadLines(CsvPath).First();
        Assert.Contains("Body Radius", headerLine);
    }

    [Fact]
    public void Constructor_LeavesNewFormatFileUntouched()
    {
        Directory.CreateDirectory(_directory);
        var original =
            "Timestamp,System Name,id64,x,y,z,Body Name,Body Type,Body Mass,Body Radius,Ring Name,Ring Type,Ring Mass,innerRadius,outerRadius,Width,estimated rotation,observed rotation,video filename,submitted\r\n" +
            "2026-01-01T00:00:00Z,Test System,12345,1,2,3,Test Body,Icy body,1.5,6378,Test Ring,Icy,2.5,100000,180000,80000,1000,1050,test.mp4,True\r\n";
        File.WriteAllText(CsvPath, original);

        var store = new MeasurementCsvStore(CsvPath);
        var record = Assert.Single(store.ReadAll());

        Assert.Equal("Icy body", record.BodyType);
        Assert.Equal(1.5, record.BodyMassEarthMasses);
        Assert.Equal(6378, record.BodyRadiusKm);
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
    public void Constructor_RenamesLegacyMeasurementsCsvToRingPeriodCsv()
    {
        Directory.CreateDirectory(_directory);
        var original =
            "Timestamp,System Name,id64,x,y,z,Body Name,Body Type,Body Mass,Ring Name,Ring Type,Ring Mass,innerRadius,outerRadius,Width,estimated rotation,observed rotation,video filename,submitted\r\n" +
            "2026-01-01T00:00:00Z,Test System,12345,1,2,3,Test Body,Icy body,1.5,Test Ring,Icy,2.5,100000,180000,80000,1000,1050,test.mp4,True\r\n";
        File.WriteAllText(LegacyCsvPath, original);

        var store = new MeasurementCsvStore(CsvPath);

        Assert.False(File.Exists(LegacyCsvPath));
        Assert.True(File.Exists(CsvPath));
        var record = Assert.Single(store.ReadAll());
        Assert.Equal("Test System", record.SystemName);
    }

    [Fact]
    public void Constructor_DoesNotOverwriteRingPeriodCsvWhenBothLegacyAndCurrentFilesExist()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(LegacyCsvPath,
            "Timestamp,System Name,id64,x,y,z,Body Name,Body Type,Body Mass,Ring Name,Ring Type,Ring Mass,innerRadius,outerRadius,Width,estimated rotation,observed rotation,video filename,submitted\r\n" +
            "2026-01-01T00:00:00Z,Legacy System,1,1,2,3,Body,Icy body,1.5,Ring,Icy,2.5,100000,180000,80000,1000,1050,legacy.mp4,True\r\n");
        File.WriteAllText(CsvPath,
            "Timestamp,System Name,id64,x,y,z,Body Name,Body Type,Body Mass,Ring Name,Ring Type,Ring Mass,innerRadius,outerRadius,Width,estimated rotation,observed rotation,video filename,submitted\r\n" +
            "2026-01-02T00:00:00Z,Current System,2,1,2,3,Body,Icy body,1.5,Ring,Icy,2.5,100000,180000,80000,1000,1050,current.mp4,True\r\n");

        var store = new MeasurementCsvStore(CsvPath);

        Assert.True(File.Exists(LegacyCsvPath));
        var record = Assert.Single(store.ReadAll());
        Assert.Equal("Current System", record.SystemName);
    }
}
