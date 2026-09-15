# Security Threat Model

| Threat | Impact | Mitigation | Residual risk |
| --- | --- | --- | --- |
| Credential stuffing | Account takeover | rate limits, lockout, generic failures | distributed limits move to WAF/Redis later |
| Brute force | Password compromise | PBKDF2, failed-attempt lockout | adaptive risk scoring deferred |
| JWT theft | Unauthorized API access | short access-token lifetime, TLS in production | token binding deferred |
| Refresh-token theft/replay | Long-lived session compromise | rotation, hashed storage, reuse detection | device management deferred |
| Horizontal privilege escalation | Customer data exposure | `/customers/me` derives customer from claims | broader ABAC rules later |
| Vertical privilege escalation | Admin operation abuse | permission policies | admin provisioning workflow deferred |
| Customer enumeration | Privacy leakage | generic auth errors, no public lookup by email | timing hardening can improve |
| PII exposure | Compliance and trust impact | PII classification, logging rules | DLP scanning deferred |
| SQL injection | Data compromise | EF parameterization | raw SQL review remains required |
| Secret leakage | Platform compromise | no committed JWT secret, `.env` ignored | secret rotation automation later |
| Duplicate registration | Duplicate identities/customers | unique constraints and idempotent consumer | cross-region uniqueness later |
| Kafka event spoofing | Fake customer creation | trusted local broker, future MSK IAM/TLS | event signing deferred |
