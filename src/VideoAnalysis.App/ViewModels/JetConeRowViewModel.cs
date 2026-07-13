using VideoAnalysis.Core.Domain;

namespace VideoAnalysis.App.ViewModels;

/// <summary>A selectable neutron star/white dwarf in the resolved system - just an identity for
/// the target picker dropdown; the actual video comes from the shared library selection instead
/// of a per-row action.</summary>
public sealed class JetConeRowViewModel
{
    public JetTargetInfo Target { get; }

    public JetConeRowViewModel(JetTargetInfo target)
    {
        Target = target;
    }

    public string SystemName => Target.SystemName;
    public string BodyName => Target.BodyName;
    public string BodyType => Target.BodyType;
    public string Display => $"{BodyName} ({BodyType})";
}
