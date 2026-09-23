# Rail Simulator Authentication

Transfer endpoints support provider-style HMAC authentication.

Required headers when `RailSimulator:RequireRequestSignature` is `true`:

- `X-Rail-Client-Id`
- `X-Rail-Api-Key`
- `X-Rail-Timestamp`
- `X-Rail-Signature`

The request signature is computed over:

```text
METHOD
PATH_AND_QUERY
UNIX_TIMESTAMP
RAW_BODY
```

The default local seeded client is:

- client id: `fintech-platform`
- api key: `local-rail-api-key`
- secret: `local-rail-client-secret`

Admin/configuration endpoints require `X-Rail-Admin-Key`. Docker Compose defaults this to `local-rail-admin-key`, and it should be overridden outside local development.
