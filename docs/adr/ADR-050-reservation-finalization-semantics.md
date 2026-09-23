# ADR-050 Reservation Finalization Semantics

Reservation commit releases the operational hold only after ledger confirmation. It does not create a second debit. Finalized financial balance remains owned by Ledger.
