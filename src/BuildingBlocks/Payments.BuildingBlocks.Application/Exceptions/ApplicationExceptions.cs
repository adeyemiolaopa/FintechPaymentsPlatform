namespace Payments.BuildingBlocks.Application.Exceptions;

public abstract class ApplicationExceptionBase : Exception
{
    protected ApplicationExceptionBase(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

public sealed class ValidationException : ApplicationExceptionBase
{
    public ValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("validation_failed", "One or more validation errors occurred.")
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

public sealed class NotFoundException : ApplicationExceptionBase
{
    public NotFoundException(string resource, string identifier)
        : base("not_found", $"{resource} '{identifier}' was not found.")
    {
        Resource = resource;
        Identifier = identifier;
    }

    public string Resource { get; }

    public string Identifier { get; }
}

public sealed class ConflictException : ApplicationExceptionBase
{
    public ConflictException(string message)
        : base("conflict", message)
    {
    }
}

public sealed class UnauthorizedApplicationException : ApplicationExceptionBase
{
    public UnauthorizedApplicationException()
        : base("unauthorized", "Authentication is required.")
    {
    }
}

public sealed class ForbiddenApplicationException : ApplicationExceptionBase
{
    public ForbiddenApplicationException()
        : base("forbidden", "The current principal is not allowed to perform this operation.")
    {
    }
}
