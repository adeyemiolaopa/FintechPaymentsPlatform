using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Payments.Account.Application.Accounts;
using Payments.Account.Domain.Accounts;
using Payments.Account.Infrastructure.Persistence;
using Payments.BuildingBlocks.Application.Abstractions;
using Payments.BuildingBlocks.Application.Exceptions;
using Payments.BuildingBlocks.Messaging.Events;

namespace Payments.Account.Infrastructure.Services;

public sealed class BeneficiaryService : IBeneficiaryService
{
    private const string BeneficiaryTopic = "beneficiary.lifecycle.v1";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly AccountDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly IRequestContext _requestContext;
    private readonly IValidator<CreateBeneficiaryRequest> _validator;

    public BeneficiaryService(AccountDbContext dbContext, IClock clock, ICurrentUser currentUser, IRequestContext requestContext, IValidator<CreateBeneficiaryRequest> validator)
    {
        _dbContext = dbContext;
        _clock = clock;
        _currentUser = currentUser;
        _requestContext = requestContext;
        _validator = validator;
    }

    public async Task<BeneficiaryResponse> AddAsync(CreateBeneficiaryRequest request, CancellationToken cancellationToken = default)
    {
        await _validator.ValidateAndThrowAsync(request, cancellationToken).ConfigureAwait(false);
        var customerId = RequireCustomerId();
        var now = _clock.UtcNow;
        var beneficiary = Beneficiary.Create(customerId, Enum.Parse<BeneficiaryType>(request.Type, true), request.Name, request.BankCode, request.AccountNumber, Currency.FromCode(request.Currency), request.CountryCode, request.Nickname, now);
        _dbContext.Beneficiaries.Add(beneficiary);
        _dbContext.AccountAuditEvents.Add(AccountAuditEvent.Create("BeneficiaryAdded", null, customerId, RequireUserIdOrNull(), now, _requestContext.CorrelationId, null, JsonSerializer.Serialize(new { beneficiaryId = beneficiary.Id, type = beneficiary.Type.ToString() })));
        AddOutbox(beneficiary, "Active", now);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("An active beneficiary already exists for this customer and destination.");
        }

        return Map(beneficiary);
    }

    public async Task<PagedResponse<BeneficiaryResponse>> ListAsync(int page, int pageSize, string? status, string? type, CancellationToken cancellationToken = default)
    {
        var customerId = RequireCustomerId();
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, 100);
        var query = _dbContext.Beneficiaries.AsNoTracking().Where(beneficiary => beneficiary.CustomerId == customerId);
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<BeneficiaryStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(beneficiary => beneficiary.Status == parsedStatus);
        }

        if (!string.IsNullOrWhiteSpace(type) && Enum.TryParse<BeneficiaryType>(type, true, out var parsedType))
        {
            query = query.Where(beneficiary => beneficiary.Type == parsedType);
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query.OrderByDescending(beneficiary => beneficiary.CreatedAtUtc).Skip((page - 1) * pageSize).Take(pageSize).Select(beneficiary => Map(beneficiary)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new PagedResponse<BeneficiaryResponse>(items, page, pageSize, total);
    }

    public async Task RemoveAsync(Guid beneficiaryId, CancellationToken cancellationToken = default)
    {
        var customerId = RequireCustomerId();
        var now = _clock.UtcNow;
        var beneficiary = await _dbContext.Beneficiaries.SingleOrDefaultAsync(item => item.Id == beneficiaryId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Beneficiary", beneficiaryId.ToString("D"));
        if (beneficiary.CustomerId != customerId)
        {
            throw new ForbiddenApplicationException();
        }

        beneficiary.Remove(now);
        _dbContext.AccountAuditEvents.Add(AccountAuditEvent.Create("BeneficiaryRemoved", null, customerId, RequireUserIdOrNull(), now, _requestContext.CorrelationId, null, JsonSerializer.Serialize(new { beneficiaryId = beneficiary.Id })));
        AddOutbox(beneficiary, "Removed", now);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private void AddOutbox(Beneficiary beneficiary, string status, DateTimeOffset now)
    {
        var envelope = new IntegrationEventEnvelope<BeneficiaryLifecycleIntegrationEvent>(Guid.NewGuid(), BeneficiaryLifecycleIntegrationEvent.EventType, BeneficiaryLifecycleIntegrationEvent.EventVersion, now, _requestContext.CorrelationId, _requestContext.CausationId, "account-service", new BeneficiaryLifecycleIntegrationEvent(beneficiary.Id, beneficiary.CustomerId, beneficiary.Type.ToString(), status, beneficiary.Currency.Code, beneficiary.CountryCode));
        _dbContext.OutboxMessages.Add(OutboxMessage.Create(BeneficiaryTopic, beneficiary.CustomerId.ToString("D"), envelope.EventType, JsonSerializer.Serialize(envelope, SerializerOptions), now));
    }

    private Guid RequireCustomerId() => Guid.TryParse(_currentUser.CustomerId, out var customerId) ? customerId : throw new UnauthorizedApplicationException();
    private Guid? RequireUserIdOrNull() => Guid.TryParse(_currentUser.UserId, out var userId) ? userId : null;
    private static BeneficiaryResponse Map(Beneficiary beneficiary) => new(beneficiary.Id, beneficiary.Type.ToString(), beneficiary.Name, beneficiary.BankCode, Mask(beneficiary.AccountNumber), beneficiary.Currency.Code, beneficiary.CountryCode, beneficiary.Nickname, beneficiary.Status.ToString(), beneficiary.CreatedAtUtc);
    private static string Mask(string accountNumber) => accountNumber.Length <= 4 ? "****" : $"******{accountNumber[^4..]}";
}
