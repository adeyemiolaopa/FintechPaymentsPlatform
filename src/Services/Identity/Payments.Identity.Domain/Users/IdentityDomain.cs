using Payments.BuildingBlocks.Domain.Primitives;

namespace Payments.Identity.Domain.Users;

public enum UserStatus
{
    PendingVerification = 0,
    Active = 1,
    Suspended = 2,
    Locked = 3,
    Disabled = 4,
}

public sealed class User : AggregateRoot<Guid>
{
    private readonly List<RefreshToken> _refreshTokens = [];

    private User() : base(Guid.Empty) { }

    private User(Guid id, Guid customerId, string email, string phoneNumber, string passwordHash, DateTimeOffset now)
        : base(id)
    {
        CustomerId = customerId;
        Email = email;
        NormalizedEmail = NormalizeEmail(email);
        PhoneNumber = NormalizePhone(phoneNumber);
        PasswordHash = passwordHash;
        Status = UserStatus.Active;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
        RaiseDomainEvent(new UserRegisteredDomainEvent(Guid.NewGuid(), id, customerId, NormalizedEmail, PhoneNumber, now));
    }

    public Guid CustomerId { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string NormalizedEmail { get; private set; } = string.Empty;
    public string PhoneNumber { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public UserStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public DateTimeOffset? LastLoginAtUtc { get; private set; }
    public int FailedLoginCount { get; private set; }
    public DateTimeOffset? FirstFailedLoginAtUtc { get; private set; }
    public DateTimeOffset? LockoutEndAtUtc { get; private set; }
    public ICollection<UserRole> UserRoles { get; private set; } = new List<UserRole>();
    public IReadOnlyCollection<RefreshToken> RefreshTokens => _refreshTokens;

    public static User Register(Guid customerId, string email, string phoneNumber, string passwordHash, DateTimeOffset now)
        => new(Guid.NewGuid(), customerId, email.Trim(), phoneNumber, passwordHash, now);

    public void AssignRole(Role role)
    {
        if (UserRoles.Any(userRole => userRole.RoleId == role.Id))
        {
            return;
        }

        UserRoles.Add(new UserRole(Id, role.Id));
    }

    public bool CanAuthenticate(DateTimeOffset now) => Status == UserStatus.Active && (LockoutEndAtUtc is null || LockoutEndAtUtc <= now);

    public void RecordSuccessfulLogin(DateTimeOffset now)
    {
        if (Status == UserStatus.Locked && LockoutEndAtUtc <= now)
        {
            Status = UserStatus.Active;
        }

        FailedLoginCount = 0;
        FirstFailedLoginAtUtc = null;
        LockoutEndAtUtc = null;
        LastLoginAtUtc = now;
        UpdatedAtUtc = now;
    }

    public void RecordFailedLogin(DateTimeOffset now, int threshold, TimeSpan window, TimeSpan lockoutDuration)
    {
        if (FirstFailedLoginAtUtc is null || now - FirstFailedLoginAtUtc > window)
        {
            FirstFailedLoginAtUtc = now;
            FailedLoginCount = 0;
        }

        FailedLoginCount++;
        UpdatedAtUtc = now;
        if (FailedLoginCount >= threshold)
        {
            Status = UserStatus.Locked;
            LockoutEndAtUtc = now.Add(lockoutDuration);
        }
    }

    public RefreshToken IssueRefreshToken(string tokenHash, string? ipAddress, DateTimeOffset now, DateTimeOffset expiresAtUtc, Guid? familyId = null)
    {
        var token = RefreshToken.Issue(Id, tokenHash, ipAddress, now, expiresAtUtc, familyId);
        _refreshTokens.Add(token);
        UpdatedAtUtc = now;
        return token;
    }

    public void Suspend(DateTimeOffset now)
    {
        if (Status == UserStatus.Disabled)
        {
            throw new DomainException("identity.user.disabled", "Disabled users cannot be suspended.");
        }

        Status = UserStatus.Suspended;
        UpdatedAtUtc = now;
    }

    public void Activate(DateTimeOffset now)
    {
        if (Status is UserStatus.Disabled)
        {
            throw new DomainException("identity.user.disabled", "Disabled users cannot be reactivated.");
        }

        Status = UserStatus.Active;
        LockoutEndAtUtc = null;
        UpdatedAtUtc = now;
    }

    public static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();

    public static string NormalizePhone(string phoneNumber) => phoneNumber.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
}

public sealed record UserRegisteredDomainEvent(Guid EventId, Guid UserId, Guid CustomerId, string Email, string PhoneNumber, DateTimeOffset OccurredAtUtc) : IDomainEvent;

public sealed class Role
{
    private Role() { }

    public Role(Guid id, string name)
    {
        Id = id;
        Name = name;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public ICollection<RolePermission> RolePermissions { get; private set; } = new List<RolePermission>();
}

public sealed class Permission
{
    private Permission() { }

    public Permission(Guid id, string name)
    {
        Id = id;
        Name = name;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
}

public sealed class UserRole
{
    private UserRole() { }

    public UserRole(Guid userId, Guid roleId)
    {
        UserId = userId;
        RoleId = roleId;
    }

    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
    public Role Role { get; private set; } = null!;
}

public sealed class RolePermission
{
    private RolePermission() { }

    public RolePermission(Guid roleId, Guid permissionId)
    {
        RoleId = roleId;
        PermissionId = permissionId;
    }

    public Guid RoleId { get; private set; }
    public Guid PermissionId { get; private set; }
    public Permission Permission { get; private set; } = null!;
}

public sealed class RefreshToken
{
    private RefreshToken() { }

    private RefreshToken(Guid userId, string tokenHash, string? createdByIp, DateTimeOffset now, DateTimeOffset expiresAtUtc, Guid? familyId)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        TokenHash = tokenHash;
        FamilyId = familyId ?? Id;
        CreatedAtUtc = now;
        ExpiresAtUtc = expiresAtUtc;
        CreatedByIp = createdByIp;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid FamilyId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public Guid? ReplacedByTokenId { get; private set; }
    public string? CreatedByIp { get; private set; }
    public string? RevokedByIp { get; private set; }
    public bool ReuseDetected { get; private set; }

    public bool IsExpired(DateTimeOffset now) => ExpiresAtUtc <= now;
    public bool IsRevoked => RevokedAtUtc is not null;
    public bool IsActive(DateTimeOffset now) => !IsRevoked && !IsExpired(now);

    public static RefreshToken Issue(Guid userId, string tokenHash, string? createdByIp, DateTimeOffset now, DateTimeOffset expiresAtUtc, Guid? familyId = null)
        => new(userId, tokenHash, createdByIp, now, expiresAtUtc, familyId);

    public void Revoke(DateTimeOffset now, string? ipAddress, Guid? replacedByTokenId = null)
    {
        if (RevokedAtUtc is not null)
        {
            return;
        }

        RevokedAtUtc = now;
        RevokedByIp = ipAddress;
        ReplacedByTokenId = replacedByTokenId;
    }

    public void MarkReuseDetected(DateTimeOffset now, string? ipAddress)
    {
        ReuseDetected = true;
        Revoke(now, ipAddress);
    }
}

public sealed class OutboxMessage
{
    private OutboxMessage() { }

    private OutboxMessage(string topic, string key, string eventType, string payload, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        Topic = topic;
        Key = key;
        EventType = eventType;
        Payload = payload;
        OccurredAtUtc = now;
    }

    public Guid Id { get; private set; }
    public string Topic { get; private set; } = string.Empty;
    public string Key { get; private set; } = string.Empty;
    public string EventType { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public DateTimeOffset? PublishedAtUtc { get; private set; }
    public int FailureCount { get; private set; }
    public string? LastError { get; private set; }

    public static OutboxMessage Create(string topic, string key, string eventType, string payload, DateTimeOffset now)
        => new(topic, key, eventType, payload, now);

    public void MarkPublished(DateTimeOffset now)
    {
        PublishedAtUtc = now;
        LastError = null;
    }

    public void MarkFailed(string error)
    {
        FailureCount++;
        LastError = error.Length > 512 ? error[..512] : error;
    }
}

public sealed class SecurityAuditEvent
{
    private SecurityAuditEvent() { }

    private SecurityAuditEvent(string eventType, Guid? actorUserId, Guid? targetUserId, Guid? targetCustomerId, DateTimeOffset now, string correlationId, string? ipAddress, string? userAgent, string metadata)
    {
        Id = Guid.NewGuid();
        EventType = eventType;
        ActorUserId = actorUserId;
        TargetUserId = targetUserId;
        TargetCustomerId = targetCustomerId;
        OccurredAtUtc = now;
        CorrelationId = correlationId;
        IpAddress = ipAddress;
        UserAgent = userAgent;
        Metadata = metadata;
    }

    public Guid Id { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public Guid? ActorUserId { get; private set; }
    public Guid? TargetUserId { get; private set; }
    public Guid? TargetCustomerId { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public string Metadata { get; private set; } = "{}";

    public static SecurityAuditEvent Create(string eventType, Guid? actorUserId, Guid? targetUserId, Guid? targetCustomerId, DateTimeOffset now, string correlationId, string? ipAddress, string? userAgent, string metadata = "{}")
        => new(eventType, actorUserId, targetUserId, targetCustomerId, now, correlationId, ipAddress, userAgent, metadata);
}




