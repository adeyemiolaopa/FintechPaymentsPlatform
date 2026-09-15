# Payment vs Ledger

Payment records intent and lifecycle. Ledger records authoritative financial postings.

A payment cannot reach Completed without a confirmed ledger transaction id. Failed or rejected payments must not create unintended ledger postings. Retrying ledger posting uses the stable PaymentId reference and must not create duplicates.