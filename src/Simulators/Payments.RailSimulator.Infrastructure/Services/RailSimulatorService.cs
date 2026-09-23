using Microsoft.EntityFrameworkCore;
using Payments.BuildingBlocks.Application.Abstractions;
using Payments.RailSimulator.Application;
using Payments.RailSimulator.Domain;
using Payments.RailSimulator.Infrastructure.Persistence;

namespace Payments.RailSimulator.Infrastructure.Services;

public sealed class RailSimulatorService : IRailSimulatorService
{
    private static readonly BankDirectoryResponse[] Banks =
    [
        new("011", "First Bank Simulator", true),
        new("044", "Access Bank Simulator", true),
        new("058", "GTBank Simulator", true),
        new("999", "Unavailable Bank Simulator", false),
    ];

    private readonly RailSimulatorDbContext _dbContext;
    private readonly IClock _clock;

    public RailSimulatorService(RailSimulatorDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<RailTransferResponse> SubmitAsync(SubmitRailTransferRequest request, RailRequestContext context, CancellationToken cancellationToken = default)
    {
        ValidateSubmit(request);
        var scenario = await GetScenarioEntityAsync(cancellationToken).ConfigureAwait(false);
        var mode = context.ScenarioOverride ?? scenario.Mode;
        var now = _clock.UtcNow;
        var requestHash = RailRequestHasher.Compute(request.ClientReference, request.DestinationBankCode, request.DestinationAccountNumber, request.DestinationAccountName, request.Amount, request.Currency, request.Narration);

        if (mode is RailScenarioMode.Outage)
        {
            await AuditAsync("provider.outage", null, request.ClientReference, "Provider outage response emitted.", cancellationToken, saveNow: true).ConfigureAwait(false);
            throw new RailSimulatorException("rail.outage", "Rail provider is unavailable.", 503);
        }

        if (mode is RailScenarioMode.Throttled)
        {
            await AuditAsync("provider.throttled", null, request.ClientReference, "Provider throttling response emitted.", cancellationToken, saveNow: true).ConfigureAwait(false);
            throw new RailSimulatorException("rail.throttled", "Rail provider throttled the request.", 429);
        }

        if (mode is RailScenarioMode.TimeoutBeforeProcessing)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(scenario.TimeoutDelayMs), cancellationToken).ConfigureAwait(false);
            throw new RailSimulatorException("rail.timeout", "Rail provider timed out before accepting the transfer.", 504);
        }

        if (mode is RailScenarioMode.Http500BeforeProcessing)
        {
            throw new RailSimulatorException("rail.http_500", "Rail provider returned an internal error before accepting the transfer.", 500);
        }

        var existing = await _dbContext.RailTransfers
            .FirstOrDefaultAsync(transfer => transfer.ClientId == context.ClientId && transfer.ClientReference == request.ClientReference, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            if (!existing.SameFinancialInstruction(requestHash))
            {
                throw new RailSimulatorException("rail.idempotency_conflict", "Client reference already exists for a different transfer instruction.", 409);
            }

            return ToTransferResponse(existing);
        }

        var transfer = RailTransfer.Create(
            RailProviderReference.Generate(now),
            context.ClientId,
            request.ClientReference,
            request.SourceInstitution,
            request.DestinationBankCode,
            request.DestinationAccountNumber,
            request.DestinationAccountName,
            request.Amount,
            request.Currency,
            request.Narration,
            mode,
            requestHash,
            now);

        ConfigureTransferCallbacks(transfer, scenario, mode, now);
        ApplyInitialScenarioOutcome(transfer, scenario, mode, now);
        _dbContext.RailTransfers.Add(transfer);
        await AuditAsync("transfer.received", transfer.ProviderReference, transfer.ClientReference, $"Transfer received with scenario {mode}.", cancellationToken).ConfigureAwait(false);
        await SaveWithIdempotencyRetryAsync(transfer, requestHash, cancellationToken).ConfigureAwait(false);

        if (transfer.Status is RailTransferStatus.Successful or RailTransferStatus.Failed)
        {
            await ScheduleTerminalSideEffectsAsync(transfer, cancellationToken).ConfigureAwait(false);
        }

        if (mode is RailScenarioMode.TimeoutAfterProcessing)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(scenario.TimeoutDelayMs), cancellationToken).ConfigureAwait(false);
            throw new RailSimulatorException("rail.timeout_after_processing", "Rail provider accepted the transfer but the client timed out.", 504);
        }

        if (mode is RailScenarioMode.Http500AfterProcessing)
        {
            throw new RailSimulatorException("rail.http_500_after_processing", "Rail provider accepted the transfer but returned a server error.", 500);
        }

        return ToTransferResponse(transfer);
    }

    public async Task<RailStatusResponse> GetByProviderReferenceAsync(string providerReference, CancellationToken cancellationToken = default)
    {
        var transfer = await FindTransferByProviderReferenceAsync(providerReference, cancellationToken).ConfigureAwait(false);
        return await ToStatusResponseAsync(transfer, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RailStatusResponse> GetByClientReferenceAsync(string clientId, string clientReference, CancellationToken cancellationToken = default)
    {
        var transfer = await _dbContext.RailTransfers.FirstOrDefaultAsync(item => item.ClientId == clientId && item.ClientReference == clientReference, cancellationToken).ConfigureAwait(false)
            ?? throw new RailSimulatorException("rail.not_found", "Rail transfer was not found.", 404);
        return await ToStatusResponseAsync(transfer, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProviderScenarioResponse> GetScenarioAsync(CancellationToken cancellationToken = default)
    {
        var scenario = await GetScenarioEntityAsync(cancellationToken).ConfigureAwait(false);
        return ToScenarioResponse(scenario);
    }

    public async Task<ProviderScenarioResponse> ConfigureScenarioAsync(ProviderScenarioRequest request, CancellationToken cancellationToken = default)
    {
        var mode = ParseEnum<RailScenarioMode>(request.Mode, "scenario mode");
        var health = ParseEnum<ProviderHealthState>(request.HealthState, "health state");
        var scenario = await GetScenarioEntityAsync(cancellationToken).ConfigureAwait(false);
        scenario.Configure(mode, request.CallbackEnabled, request.DuplicateCallbackCount, request.DelayMs, request.TimeoutDelayMs, health, _clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToScenarioResponse(scenario);
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await _dbContext.CallbackAttempts.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await _dbContext.SettlementRecords.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await _dbContext.RequestAuditEvents.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await _dbContext.RailTransfers.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

        var scenario = await GetScenarioEntityAsync(cancellationToken).ConfigureAwait(false);
        scenario.Configure(RailScenarioMode.Success, true, 1, 100, 1500, ProviderHealthState.Available, _clock.UtcNow);
        var callback = await GetCallbackConfigurationEntityAsync(cancellationToken).ConfigureAwait(false);
        callback.Configure(string.Empty, callback.WebhookSecret, _clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<CallbackConfigurationResponse> ConfigureCallbackAsync(CallbackConfigurationRequest request, CancellationToken cancellationToken = default)
    {
        var callback = await GetCallbackConfigurationEntityAsync(cancellationToken).ConfigureAwait(false);
        callback.Configure(request.Url, request.WebhookSecret, _clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new CallbackConfigurationResponse(callback.Url, callback.UpdatedAtUtc);
    }

    public async Task<ProviderStatusResponse> GetProviderStatusAsync(CancellationToken cancellationToken = default)
    {
        var scenario = await GetScenarioEntityAsync(cancellationToken).ConfigureAwait(false);
        return new ProviderStatusResponse(scenario.HealthState.ToString(), scenario.Mode.ToString(), _clock.UtcNow);
    }

    public async Task<IReadOnlyCollection<CallbackAttemptResponse>> GetCallbackAttemptsAsync(string providerReference, CancellationToken cancellationToken = default)
    {
        var transfer = await FindTransferByProviderReferenceAsync(providerReference, cancellationToken).ConfigureAwait(false);
        return await _dbContext.CallbackAttempts
            .Where(callback => callback.RailTransferId == transfer.Id)
            .OrderBy(callback => callback.AttemptNumber)
            .Select(callback => new CallbackAttemptResponse(callback.Id, callback.RailTransferId, callback.CallbackEventId, callback.AttemptNumber, callback.TargetUrl, callback.Status.ToString(), callback.StatusCode, callback.StartedAtUtc, callback.CompletedAtUtc, callback.NextAttemptAtUtc, callback.Error))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<SettlementRecordResponse>> GenerateSettlementAsync(DateOnly settlementDate, string anomaly = "normal", CancellationToken cancellationToken = default)
    {
        var startUtc = new DateTimeOffset(settlementDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var endUtc = startUtc.AddDays(1);
        var transfers = await _dbContext.RailTransfers
            .Where(transfer => transfer.Status == RailTransferStatus.Successful && transfer.CompletedAtUtc >= startUtc && transfer.CompletedAtUtc < endUtc)
            .OrderBy(transfer => transfer.ProviderReference)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var records = transfers.Select(transfer => SettlementRecord.FromTransfer(transfer, settlementDate)).ToList();
        var responses = records.Select(ToSettlementResponse).ToList();

        switch ((anomaly ?? "normal").Trim().ToLowerInvariant())
        {
            case "normal":
                break;
            case "missing":
                if (responses.Count > 0) responses.RemoveAt(0);
                break;
            case "duplicate":
                if (responses.Count > 0) responses.Add(responses[0]);
                break;
            case "amount_mismatch":
                if (responses.Count > 0)
                {
                    var first = responses[0];
                    responses[0] = first with { Amount = first.Amount + 1m };
                }
                break;
            case "unknown_reference":
                responses.Add(new SettlementRecordResponse($"RAIL-NG-{settlementDate:yyyyMMdd}-UNKNOWN", $"UNKNOWN-{Guid.NewGuid():N}"[..24], 1m, "NGN", RailTransferStatus.Successful.ToString(), settlementDate));
                break;
            default:
                throw new RailSimulatorException("rail.settlement_anomaly", "Unsupported settlement anomaly.", 400);
        }

        return responses;
    }

    public Task<NameEnquiryResponse> NameEnquiryAsync(NameEnquiryRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scenario = (request.Scenario ?? "success").Trim().ToLowerInvariant();
        if (scenario is "timeout") throw new RailSimulatorException("rail.name_enquiry_timeout", "Name enquiry timed out.", 504);
        if (scenario is "not_found" or "notfound" or "invalid") return Task.FromResult(new NameEnquiryResponse(request.BankCode, Mask(request.AccountNumber), string.Empty, "Unknown", "07", "Account not found"));

        var bank = Banks.FirstOrDefault(item => item.BankCode == request.BankCode);
        if (bank is null || !bank.IsAvailable) return Task.FromResult(new NameEnquiryResponse(request.BankCode, Mask(request.AccountNumber), string.Empty, "Unavailable", "91", "Bank unavailable"));

        var suffix = request.AccountNumber.Length >= 4 ? request.AccountNumber[^4..] : request.AccountNumber;
        return Task.FromResult(new NameEnquiryResponse(request.BankCode, Mask(request.AccountNumber), $"RAIL TEST {suffix}", "Active", RailResponseCodes.Successful, "Successful"));
    }

    public Task<IReadOnlyCollection<BankDirectoryResponse>> GetBanksAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyCollection<BankDirectoryResponse>>(Banks);
    }

    public async Task<int> CompleteDueTransfersAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var dueTransfers = await _dbContext.RailTransfers
            .Where(transfer => transfer.Status == RailTransferStatus.Processing && transfer.NextProcessingAtUtc != null && transfer.NextProcessingAtUtc <= now)
            .OrderBy(transfer => transfer.NextProcessingAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var transfer in dueTransfers)
        {
            if (transfer.ConfiguredScenario is RailScenarioMode.PendingThenFailure)
            {
                transfer.MarkFailed("51", "Simulated delayed failure.", now);
            }
            else
            {
                transfer.MarkSuccessful(now);
            }

            await ScheduleTerminalSideEffectsAsync(transfer, cancellationToken).ConfigureAwait(false);
        }

        return dueTransfers.Count;
    }

    private async Task SaveWithIdempotencyRetryAsync(RailTransfer transfer, string requestHash, CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            _dbContext.ChangeTracker.Clear();
            var existing = await _dbContext.RailTransfers.FirstOrDefaultAsync(item => item.ClientId == transfer.ClientId && item.ClientReference == transfer.ClientReference, cancellationToken).ConfigureAwait(false);
            if (existing is not null && existing.SameFinancialInstruction(requestHash)) return;
            throw new RailSimulatorException("rail.idempotency_conflict", "Client reference already exists for a different transfer instruction.", 409);
        }
    }

    private async Task<RailStatusResponse> ToStatusResponseAsync(RailTransfer transfer, CancellationToken cancellationToken)
    {
        if (transfer.ConfiguredScenario is RailScenarioMode.InconsistentStatus && transfer.Status is RailTransferStatus.Successful)
        {
            var count = transfer.IncrementStatusQueryCount();
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (count == 1) return ToStatusResponse(transfer, RailTransferStatus.Processing);
            if (count == 2) return ToStatusResponse(transfer, RailTransferStatus.Unknown);
        }

        return ToStatusResponse(transfer);
    }

    private void ApplyInitialScenarioOutcome(RailTransfer transfer, ProviderScenario scenario, RailScenarioMode mode, DateTimeOffset now)
    {
        switch (mode)
        {
            case RailScenarioMode.Failure:
                transfer.MarkProcessing(now);
                transfer.MarkFailed("51", "Simulated failure.", now);
                break;
            case RailScenarioMode.PendingThenSuccess:
            case RailScenarioMode.PendingThenFailure:
                transfer.MarkProcessing(now, now.AddMilliseconds(scenario.DelayMs));
                break;
            default:
                transfer.MarkProcessing(now);
                transfer.MarkSuccessful(now);
                break;
        }
    }

    private void ConfigureTransferCallbacks(RailTransfer transfer, ProviderScenario scenario, RailScenarioMode mode, DateTimeOffset now)
    {
        if (!scenario.CallbackEnabled || mode is RailScenarioMode.NoCallback)
        {
            transfer.DisableCallbacks();
            return;
        }

        var duplicateCount = mode is RailScenarioMode.DuplicateCallback ? Math.Max(2, scenario.DuplicateCallbackCount) : scenario.DuplicateCallbackCount;
        transfer.ConfigureCallbacks(duplicateCount, now.AddMilliseconds(Math.Max(1, scenario.DelayMs)));
    }

    private async Task ScheduleTerminalSideEffectsAsync(RailTransfer transfer, CancellationToken cancellationToken)
    {
        if (transfer.Status is RailTransferStatus.Successful)
        {
            var settlementDate = DateOnly.FromDateTime((transfer.CompletedAtUtc ?? _clock.UtcNow).UtcDateTime);
            var settlementExists = await _dbContext.SettlementRecords.AnyAsync(record => record.ProviderReference == transfer.ProviderReference, cancellationToken).ConfigureAwait(false);
            if (!settlementExists)
            {
                _dbContext.SettlementRecords.Add(SettlementRecord.FromTransfer(transfer, settlementDate));
            }
        }

        if (transfer.CallbackEnabled)
        {
            var callback = await GetCallbackConfigurationEntityAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(callback.Url))
            {
                var alreadyScheduled = await _dbContext.CallbackAttempts.AnyAsync(item => item.RailTransferId == transfer.Id, cancellationToken).ConfigureAwait(false);
                if (!alreadyScheduled)
                {
                    for (var index = 1; index <= transfer.DuplicateCallbackCount; index++)
                    {
                        _dbContext.CallbackAttempts.Add(CallbackAttempt.Schedule(transfer.Id, transfer.CallbackEventId, index, callback.Url, transfer.CallbackScheduledAtUtc ?? _clock.UtcNow, _clock.UtcNow));
                    }
                    transfer.MarkCallbackScheduled(_clock.UtcNow);
                }
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ProviderScenario> GetScenarioEntityAsync(CancellationToken cancellationToken)
    {
        var scenario = await _dbContext.ProviderScenarios.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (scenario is not null) return scenario;

        scenario = ProviderScenario.Default(_clock.UtcNow);
        _dbContext.ProviderScenarios.Add(scenario);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return scenario;
    }

    private async Task<CallbackConfiguration> GetCallbackConfigurationEntityAsync(CancellationToken cancellationToken)
    {
        var callback = await _dbContext.CallbackConfigurations.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (callback is not null) return callback;

        callback = CallbackConfiguration.Default(_clock.UtcNow);
        _dbContext.CallbackConfigurations.Add(callback);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return callback;
    }

    private async Task<RailTransfer> FindTransferByProviderReferenceAsync(string providerReference, CancellationToken cancellationToken)
        => await _dbContext.RailTransfers.FirstOrDefaultAsync(item => item.ProviderReference == providerReference, cancellationToken).ConfigureAwait(false)
            ?? throw new RailSimulatorException("rail.not_found", "Rail transfer was not found.", 404);

    private async Task AuditAsync(string eventType, string? providerReference, string? clientReference, string message, CancellationToken cancellationToken, bool saveNow = false)
    {
        _dbContext.RequestAuditEvents.Add(RequestAuditEvent.Create(eventType, providerReference, clientReference, message, _clock.UtcNow));
        if (saveNow)
        {
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static void ValidateSubmit(SubmitRailTransferRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ClientReference)) throw new RailSimulatorException("rail.client_reference", "Client reference is required.");
        if (string.IsNullOrWhiteSpace(request.DestinationBankCode)) throw new RailSimulatorException("rail.bank_code", "Destination bank code is required.");
        if (string.IsNullOrWhiteSpace(request.DestinationAccountNumber)) throw new RailSimulatorException("rail.account_number", "Destination account number is required.");
        if (request.Amount <= 0m) throw new RailSimulatorException("rail.amount", "Amount must be greater than zero.");
    }

    private static TEnum ParseEnum<TEnum>(string value, string name) where TEnum : struct
    {
        var normalized = value.Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        if (Enum.TryParse<TEnum>(normalized, true, out var parsed)) return parsed;
        throw new RailSimulatorException("rail.invalid_configuration", $"Unsupported {name}.", 400);
    }

    private static RailTransferResponse ToTransferResponse(RailTransfer transfer)
    {
        var response = RailResponseCodes.ForStatus(transfer.Status);
        return new RailTransferResponse(transfer.ProviderReference, transfer.ClientReference, transfer.Status.ToString(), response.Code, response.Message, transfer.Amount, transfer.Currency, transfer.ProcessedAtUtc, transfer.CompletedAtUtc);
    }

    private static RailStatusResponse ToStatusResponse(RailTransfer transfer, RailTransferStatus? statusOverride = null)
        => new(transfer.ProviderReference, transfer.ClientReference, (statusOverride ?? transfer.Status).ToString(), transfer.Amount, transfer.Currency, transfer.ReceivedAtUtc, transfer.ProcessedAtUtc, transfer.CompletedAtUtc, transfer.FailureCode, transfer.FailureReason);

    private static ProviderScenarioResponse ToScenarioResponse(ProviderScenario scenario)
        => new(scenario.Mode.ToString(), scenario.CallbackEnabled, scenario.DuplicateCallbackCount, scenario.DelayMs, scenario.TimeoutDelayMs, scenario.HealthState.ToString(), scenario.UpdatedAtUtc);

    private static SettlementRecordResponse ToSettlementResponse(SettlementRecord record)
        => new(record.ProviderReference, record.ClientReference, record.Amount, record.Currency, record.Status, record.SettlementDate);

    private static string Mask(string accountNumber)
    {
        var trimmed = accountNumber.Trim();
        return trimmed.Length <= 4 ? "****" : $"******{trimmed[^4..]}";
    }
}
