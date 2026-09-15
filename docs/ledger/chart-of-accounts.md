# Chart Of Accounts

Week 4 introduces a minimal transaction-ledger chart of accounts, not a complete accounting suite.

```text
1000 Assets
  1100 Settlement Assets
  1200 Clearing Assets

2000 Liabilities
  2100 Customer Wallet Liabilities

4000 Revenue
  4100 Transaction Fee Revenue

9000 Suspense / Control
```

Development seed accounts include `NGN_SETTLEMENT_ASSET`, `NGN_CLEARING`, `NGN_FEE_REVENUE`, and `NGN_SUSPENSE` equivalents. Suspense is for unresolved accounting destinations that require later reconciliation; it is not a generic error bucket.

Normal balances: Asset and Expense accounts are debit-normal. Liability, Equity, and Revenue accounts are credit-normal.