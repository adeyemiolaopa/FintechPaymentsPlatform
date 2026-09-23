# Settlement files

Upload is restricted to `reconciliation.file.upload` and accepts UTF-8 CSV with `provider_reference,client_reference,amount,currency,status,settlement_date`. The SHA-256 of the raw bytes plus provider identifies an import. Filename is metadata, not identity. A changed file with the same filename is a new import. A malformed middle row rejects the entire file before matching, never silently accepting a valid prefix.

The store streams uploads with a size limit. Validation streams the CSV and checks the requested business date. Staging inserts bounded database batches with unique file/line identity; matching advances `LastProcessedLine` after each committed batch. Restart resumes from the last committed line. PostgreSQL row locks and leases prevent simultaneous workers from claiming a file. Duplicate provider references are classified, not double-counted as matches. Control totals are not present in the initial simulator format; a provider format with control totals must validate them before processing.

State: `Validating` during parse; `Rejected` for malformed input; `Processing` while batches or reverse matching remain; `Completed` when all records agree; `CompletedWithExceptions` when any discrepancy exists. Operational failures leave the import resumable. Use provider business date, not payment initiation UTC date, for file-level controls. Successful monetary totals must be grouped by currency; do not aggregate NGN, USD and GHS into one number.

The local implementation renews processing leases on every staging batch. A 100k-row PostgreSQL integration baseline is measured during verification; do not infer production capacity from local hardware. The optional 1m-row stress run and production S3 store remain deployment exercises.
