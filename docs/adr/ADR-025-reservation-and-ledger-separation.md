# ADR-025 Reservation and Ledger Separation

Status: Accepted

Account reservations are holds and are not ledger debits. Ledger postings are the financial source of truth. Reservation commit removes a hold after ledger confirmation and does not independently create financial movement.
