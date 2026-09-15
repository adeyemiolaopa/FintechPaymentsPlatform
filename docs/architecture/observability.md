# Observability Notes

A trace is an end-to-end path through the system. A span is one timed operation within that path. Baggage carries small cross-process values for observability context. A correlation ID is an application-level identifier used to join logs and future Kafka messages for the same request or business flow.

Correlation IDs should propagate through HTTP `X-Correlation-Id` and later Kafka headers. They do not replace W3C trace context.