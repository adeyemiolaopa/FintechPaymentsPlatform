# Rail Simulator Webhook Signatures

Callbacks are posted as JSON to the configured callback URL. Each callback includes:

- `X-Rail-Timestamp`
- `X-Rail-Signature`

The callback signature is HMAC-SHA256 over:

```text
timestamp.rawBody
```

The callback secret is configured through `PUT /api/v1/config/callback`. Failed callbacks are recorded and retried with bounded backoff until the configured maximum attempt count is reached.
