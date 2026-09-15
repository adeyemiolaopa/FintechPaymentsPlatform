# Architecture Principles

The platform starts with DDD and Clean Architecture. Domain code has no dependency on infrastructure, API, persistence, Redis, Kafka, or AWS. Application use cases depend on abstractions and coordinate workflows. Infrastructure implements adapters for databases, caches, messaging, and external systems. API endpoints translate transport concerns into application requests.

Future services must own their data and contracts. No shared database assumptions are allowed. Avoid distributed monolith behavior by keeping integration events explicit and versioned.

Domain events are internal facts raised by aggregates inside a bounded context. Integration events are service contracts exchanged through Kafka. Domain objects must not be serialized directly onto Kafka.

Correlation IDs connect logs and messages that belong to the same business/request flow. Distributed trace IDs come from OpenTelemetry and model causality across spans. They are related but not interchangeable.