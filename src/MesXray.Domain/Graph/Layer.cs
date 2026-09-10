namespace MesXray.Domain.Graph;

/// <summary>Architectural layer a node lives in. Used for progressive disclosure and layout.</summary>
public enum Layer
{
    Web,
    Api,
    Service,
    Data,
    Config,
}
