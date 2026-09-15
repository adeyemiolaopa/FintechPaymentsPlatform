# ADR-023 Explicit Payment State Machine

Status: Accepted

Payment status changes must use domain methods and validated transitions. Illegal transitions throw domain errors and every meaningful transition is persisted in `payment_state_transitions`.
