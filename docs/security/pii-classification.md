# PII Classification

PII: email, phone number, date of birth, address, and customer names. Sensitive credential data: password hashes and refresh-token hashes.

PII is stored in Identity for login/contact uniqueness and in Customer for profile ownership. Logs, traces, span attributes, and audit metadata should prefer identifiers such as `UserId`, `CustomerId`, and `CorrelationId`.

Never log plaintext passwords, access tokens, refresh tokens, signing keys, BVN/NIN, full addresses, or bank balances.
