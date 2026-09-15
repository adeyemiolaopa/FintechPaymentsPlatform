# Payment State Machine

Week 5 supports explicit states: Initiated, PendingValidation, FundsReserved, Processing, SubmittedToRail, PendingReconciliation, Completed, Failed, Rejected, and Cancelled.

Active Week 5 internal transfer path:

```text
Initiated -> PendingValidation -> FundsReserved -> Processing -> Completed
```

Business validation failures move to Rejected before financial execution. Processing failures after reservation move to Failed after reservation release when release is definitive. Completed payments cannot be cancelled; correction requires future reversal semantics.

Every state change appends a `payment_state_transitions` row and a payment audit event.