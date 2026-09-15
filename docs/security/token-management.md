# Token Management

JWT claims are limited to `sub`, `jti`, issuer/audience/timestamps, role, permission, and `customer_id`. Tokens must never contain BVN, NIN, address, date of birth, balances, passwords, or refresh-token values.

Refresh-token reuse is treated as possible theft. If a rotated token is presented again, the token family is revoked and a security audit event is recorded. Access tokens are not globally blacklisted; they expire naturally.

Production key strategy: signing secrets must come from environment-backed secret stores, then AWS Secrets Manager and KMS. The committed appsettings files intentionally do not contain a signing key.
