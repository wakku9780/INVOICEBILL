namespace SareeBilling.Application.Common;

/// <summary>
/// Thrown when user input breaks a business rule. Errors are written for
/// shop staff, so the UI can show them directly.
/// </summary>
public sealed class ValidationException : Exception
{
    public ValidationException(IReadOnlyList<string> errors)
        : base(string.Join(Environment.NewLine, errors))
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}
