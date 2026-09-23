# ADR-071: Reconciliation matching hierarchy

Accepted. Provider reference is primary, client/payment reference fallback. Amount plus destination/time is never an identity key. Both references, exact decimal amount, currency, provider outcome, Payment state and Ledger posting are compared. Contradictory evidence is an exception, not an automatic correction.
