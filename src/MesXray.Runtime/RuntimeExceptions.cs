namespace MesXray.Runtime;

/// <summary>The requested entity has no recorded runtime data. Not an error in the graph - an explicit Unknown.</summary>
public sealed class RuntimeDataUnavailableException : Exception
{
    public RuntimeDataUnavailableException(string message)
        : base(message)
    {
    }
}

/// <summary>The operation or environment is outside the whitelist.</summary>
public sealed class RuntimeAccessDeniedException : Exception
{
    public RuntimeAccessDeniedException(string message)
        : base(message)
    {
    }
}
