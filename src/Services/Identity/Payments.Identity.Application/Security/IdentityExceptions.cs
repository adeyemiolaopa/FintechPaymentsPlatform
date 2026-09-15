namespace Payments.Identity.Application.Security;

public sealed class InvalidCredentialsException : Exception
{
    public InvalidCredentialsException() : base("Invalid credentials.") { }
}

public sealed class LockedIdentityException : Exception
{
    public LockedIdentityException(DateTimeOffset? lockoutEndAtUtc) : base("Identity is temporarily locked.") => LockoutEndAtUtc = lockoutEndAtUtc;

    public DateTimeOffset? LockoutEndAtUtc { get; }
}
