# Security Audit

Operational logs explain runtime behavior and support debugging. Security audit events are append-oriented records of security-sensitive actions and lifecycle changes.

Identity records events such as `UserRegistered`, `LoginSucceeded`, `LoginFailed`, `RefreshTokenIssued`, `RefreshTokenRejected`, and `RefreshTokenRevoked`. Customer records `CustomerRegistered`, `CustomerProfileUpdated`, `CustomerSuspended`, and `CustomerActivated`.

Audit metadata must not contain secrets or raw bearer tokens.
