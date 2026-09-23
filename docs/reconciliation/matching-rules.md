# Matching rules

1. Match by provider reference, then client/payment reference. Never by amount and time alone.
2. Verify both references when present. A conflict is `REFERENCE_MISMATCH`.
3. Compare exact decimal amount and ISO currency, including ledger posting. Differences are critical exceptions and prohibit recovery.
4. Compare provider/settlement outcome to Payment status. Provider success with a failed Payment and provider failure with a completed Payment require review; never reverse automatically.
5. Require posted ledger evidence for a completed successful Payment. A pending Payment with complete success evidence may request Payment recovery; a completed Payment with missing ledger is critical.
6. Check reverse direction after the configured settlement delay: internally successful transfers missing from the file become `SETTLEMENT_RECORD_MISSING`.

`Matched` means the available required evidence agrees. `Resolved` means a controlled recovery command completed and fresh evidence agrees. `Pending` means final evidence is unavailable or an owning service/provider is down. `Exception` requires investigation. `Ignored` requires a recorded operator reason. A settlement file row being parsed is not itself proof of financial agreement.

Provider evidence is append-only. For the simulator, a settlement row is stronger than a transient status query, which is stronger than callback or synchronous response; this ordering must be configured per provider contract rather than generalized to all rails. Conflicting final observations remain exceptions.
