namespace VideoAnalysis.App.ViewModels;

/// <summary>A selectable body in the Video Upload Metadata window's Body dropdown - pairs the
/// body's name with its type so the dropdown list can show both (e.g. "Merope A (K
/// (Yellow-Orange) Star)"), same "{Name} ({Type})" convention as
/// <see cref="RingRowViewModel.BodyDisplay"/>, while <see cref="Name"/> stays the plain identity
/// used for ring lookups and persisted onto <see cref="Core.Storage.VideoLibraryEntry.BodyName"/>.</summary>
public sealed record BodyOption(string Name, string? Type)
{
    public string Display => string.IsNullOrWhiteSpace(Type) ? Name : $"{Name} ({Type})";
}
