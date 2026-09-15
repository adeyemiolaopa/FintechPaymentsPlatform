# Payment Architecture

Payment Service is the workflow and orchestration source of truth for payment intent, lifecycle state, metadata, audit trail, recovery, and payment lifecycle events.

Ownership boundaries:

- Customer Service owns customer identity, lifecycle status, and KYC status.
- Account Service owns operational account state, restrictions, and reservations.
- Ledger Service owns finalized financial postings and balances.
- Payment Service owns payment workflow state and calls Account/Ledger through service-boundary clients.

Payment Service does not write Account, Customer, or Ledger databases. Local customer and account references are read models populated from lifecycle events. They are used for validation and routing, not ownership transfer.