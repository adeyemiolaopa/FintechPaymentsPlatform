# Evidence policy

`IProviderReconciliationPolicy` defines final success/failure statuses, event-lag grace, and settlement delay for a provider. The simulator policy is a development default, not a universal rule. A production policy must also define authoritative settlement formats, NotFound interpretation, expected delivery time, cutoff, market timezone, weekends and holidays.

Preserve each provider query as an append-only `ProviderStatusObservation`. Settlement evidence can be stronger than a transient API response, but contradictory final evidence must be investigated. A Payment event may lag behind a file; defer missing-reference classification for the configured grace period. Do not delay amount or currency mismatch once both sides are present.
