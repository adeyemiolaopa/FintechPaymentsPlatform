using FluentValidation;
using Payments.BuildingBlocks.Application.Abstractions;
using Payments.BuildingBlocks.Application.Exceptions;
using Payments.Customer.Domain.Customers;

namespace Payments.Customer.Application.Customers;

public sealed record CustomerProfileResponse(
    Guid CustomerId,
    Guid IdentityUserId,
    string? FirstName,
    string? MiddleName,
    string? LastName,
    string Email,
    string PhoneNumber,
    DateOnly? DateOfBirth,
    string Status,
    string KycStatus,
    CustomerAddressResponse? Address);

public sealed record CustomerAddressResponse(string Line1, string? Line2, string City, string Region, string CountryCode, string? PostalCode);

public sealed record UpdateCustomerProfileRequest(string? FirstName, string? MiddleName, string? LastName, CustomerAddressRequest? Address);

public sealed record CustomerAddressRequest(string Line1, string? Line2, string City, string Region, string CountryCode, string? PostalCode);

public sealed record SuspendCustomerRequest(string Reason);

public interface ICustomerRepository
{
    Task<Customer.Domain.Customers.Customer?> GetByIdAsync(Guid customerId, CancellationToken cancellationToken = default);
    Task<Customer.Domain.Customers.Customer?> GetByIdentityUserIdAsync(Guid identityUserId, CancellationToken cancellationToken = default);
    Task<bool> HasProcessedEventAsync(Guid eventId, CancellationToken cancellationToken = default);
    void Add(Customer.Domain.Customers.Customer customer);
    void AddProcessedEvent(ProcessedIntegrationEvent processedEvent);
    void AddAudit(CustomerAuditEvent auditEvent);
    void AddOutbox(OutboxMessage outboxMessage);
    void AddLifecycleOutbox(Customer.Domain.Customers.Customer customer, DateTimeOffset occurredAtUtc, string correlationId, string? causationId = null);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ICustomerProfileService
{
    Task<CustomerProfileResponse> GetMeAsync(CancellationToken cancellationToken = default);
    Task UpdateMeAsync(UpdateCustomerProfileRequest request, CancellationToken cancellationToken = default);
    Task<CustomerProfileResponse> GetByIdAsync(Guid customerId, CancellationToken cancellationToken = default);
    Task SuspendAsync(Guid customerId, SuspendCustomerRequest request, CancellationToken cancellationToken = default);
    Task ActivateAsync(Guid customerId, CancellationToken cancellationToken = default);
}

public sealed class UpdateCustomerProfileRequestValidator : AbstractValidator<UpdateCustomerProfileRequest>
{
    public UpdateCustomerProfileRequestValidator()
    {
        RuleFor(request => request.FirstName).MaximumLength(80);
        RuleFor(request => request.MiddleName).MaximumLength(80);
        RuleFor(request => request.LastName).MaximumLength(80);
        RuleFor(request => request.Address!.Line1).NotEmpty().MaximumLength(160).When(request => request.Address is not null);
        RuleFor(request => request.Address!.City).NotEmpty().MaximumLength(80).When(request => request.Address is not null);
        RuleFor(request => request.Address!.Region).NotEmpty().MaximumLength(80).When(request => request.Address is not null);
        RuleFor(request => request.Address!.CountryCode).NotEmpty().Length(2).When(request => request.Address is not null);
    }
}

public sealed class CustomerProfileService : ICustomerProfileService
{
    private readonly ICustomerRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IRequestContext _requestContext;
    private readonly IValidator<UpdateCustomerProfileRequest> _validator;

    public CustomerProfileService(ICustomerRepository repository, ICurrentUser currentUser, IClock clock, IRequestContext requestContext, IValidator<UpdateCustomerProfileRequest> validator)
    {
        _repository = repository;
        _currentUser = currentUser;
        _clock = clock;
        _requestContext = requestContext;
        _validator = validator;
    }

    public async Task<CustomerProfileResponse> GetMeAsync(CancellationToken cancellationToken = default)
    {
        var customerId = RequireCustomerId();
        var customer = await _repository.GetByIdAsync(customerId, cancellationToken).ConfigureAwait(false) ?? throw new NotFoundException("Customer", customerId.ToString("D"));
        return Map(customer);
    }

    public async Task UpdateMeAsync(UpdateCustomerProfileRequest request, CancellationToken cancellationToken = default)
    {
        await _validator.ValidateAndThrowAsync(request, cancellationToken).ConfigureAwait(false);
        var customerId = RequireCustomerId();
        var actorUserId = RequireUserId();
        var customer = await _repository.GetByIdAsync(customerId, cancellationToken).ConfigureAwait(false) ?? throw new NotFoundException("Customer", customerId.ToString("D"));
        customer.UpdateProfile(request.FirstName, request.MiddleName, request.LastName, request.Address is null ? null : new CustomerAddress(request.Address.Line1, request.Address.Line2, request.Address.City, request.Address.Region, request.Address.CountryCode.ToUpperInvariant(), request.Address.PostalCode), actorUserId, _clock.UtcNow);
        _repository.AddAudit(CustomerAuditEvent.Create("CustomerProfileUpdated", actorUserId, customer.Id, _clock.UtcNow, _requestContext.CorrelationId));
        await _repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<CustomerProfileResponse> GetByIdAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        var customer = await _repository.GetByIdAsync(customerId, cancellationToken).ConfigureAwait(false) ?? throw new NotFoundException("Customer", customerId.ToString("D"));
        return Map(customer);
    }

    public async Task SuspendAsync(Guid customerId, SuspendCustomerRequest request, CancellationToken cancellationToken = default)
    {
        var actorUserId = RequireUserId();
        var customer = await _repository.GetByIdAsync(customerId, cancellationToken).ConfigureAwait(false) ?? throw new NotFoundException("Customer", customerId.ToString("D"));
        customer.Suspend(actorUserId, request.Reason, _clock.UtcNow);
        _repository.AddAudit(CustomerAuditEvent.Create("CustomerSuspended", actorUserId, customer.Id, _clock.UtcNow, _requestContext.CorrelationId));
        _repository.AddLifecycleOutbox(customer, _clock.UtcNow, _requestContext.CorrelationId, _requestContext.CausationId);
        await _repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ActivateAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        var actorUserId = RequireUserId();
        var customer = await _repository.GetByIdAsync(customerId, cancellationToken).ConfigureAwait(false) ?? throw new NotFoundException("Customer", customerId.ToString("D"));
        customer.Activate(actorUserId, _clock.UtcNow);
        _repository.AddAudit(CustomerAuditEvent.Create("CustomerActivated", actorUserId, customer.Id, _clock.UtcNow, _requestContext.CorrelationId));
        _repository.AddLifecycleOutbox(customer, _clock.UtcNow, _requestContext.CorrelationId, _requestContext.CausationId);
        await _repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private Guid RequireCustomerId() => Guid.TryParse(_currentUser.CustomerId, out var customerId) ? customerId : throw new UnauthorizedAccessException("Authenticated customer context is required.");

    private Guid RequireUserId() => Guid.TryParse(_currentUser.UserId, out var userId) ? userId : throw new UnauthorizedAccessException("Authenticated user context is required.");

    private static CustomerProfileResponse Map(Customer.Domain.Customers.Customer customer) => new(customer.Id, customer.IdentityUserId, customer.FirstName, customer.MiddleName, customer.LastName, customer.Email, customer.PhoneNumber, customer.DateOfBirth, customer.Status.ToString(), customer.KycStatus.ToString(), customer.Address is null ? null : new CustomerAddressResponse(customer.Address.Line1, customer.Address.Line2, customer.Address.City, customer.Address.Region, customer.Address.CountryCode, customer.Address.PostalCode));
}

