# ADR-073: Reconciliation evidence hierarchy

Accepted. Preserve provider queries as append-only observations. Settlement records often outrank transient status API results, callbacks and initial HTTP responses, but evidence precedence and finality are provider-specific through `IProviderReconciliationPolicy`. Conflicting final evidence remains an exception.
