# Customer Architecture

Customer owns financial-services customer profile and lifecycle state. It stores customer identity linkage, profile fields, address, customer status, KYC status foundation, audit events, and processed integration events.

Customer consumes `identity.lifecycle.v1` through the shared integration contract only. It must not reference Identity domain entities, repositories, or `IdentityDbContext`. Duplicate Kafka delivery is handled by `processed_integration_events` and a unique `IdentityUserId` constraint.

Self-service endpoints derive `CustomerId` from JWT claims. Administrative lifecycle operations require explicit permissions.
