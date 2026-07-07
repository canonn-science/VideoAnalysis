namespace RotationAnalysis.Core.Updates;

public sealed record UpdateInfo(Version Version, string ReleaseUrl, string InstallerDownloadUrl, string InstallerFileName);
