namespace SaniStock.Domain;

/// <summary>
/// A business-rule violation that should be shown to the user as a friendly message
/// rather than logged as an unexpected crash.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}
