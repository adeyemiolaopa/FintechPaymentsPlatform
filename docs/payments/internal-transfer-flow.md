# Internal Transfer Flow

Successful internal transfer:

```text
Client -> Payment API
Payment -> local customer/account reference validation
Payment -> Account Service: reserve funds using PaymentId as reference
Payment -> Ledger Service: post debit source liability and credit destination liability using PaymentId as external reference
Payment -> Account Service: commit reservation
Payment -> Payment DB: Completed
Payment -> Outbox: payment.lifecycle.changed
```

The ledger posting is the financial effect. Reservation commit only removes the operational hold and must not independently debit account balance.