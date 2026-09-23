# ADR-070: Settlement-file reconciliation model

Accepted. Validate the entire UTF-8 CSV before matching; reject malformed files. Store raw bytes behind `ISettlementFileStore`, stage rows and process bounded batches with persisted line checkpoints. A provider business date is separate from UTC timestamps. Reverse matching detects internal success absent from a delivered file after a policy delay.
