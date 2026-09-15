# Double-Entry Accounting

Every posted ledger transaction must balance by currency: total debits equal total credits. A transaction must contain at least two postings and all postings must use the transaction currency.

Wallet funding example:

```text
Debit   Settlement Asset              NGN 100,000
Credit  Customer Wallet Liability     NGN 100,000
```

The customer wallet is a liability because the institution owes that stored value to the customer.

Internal transfer example:

```text
Debit   Customer A Wallet Liability   NGN 10,000
Credit  Customer B Wallet Liability   NGN 10,000
```

Debiting a liability reduces the obligation to customer A. Crediting a liability increases the obligation to customer B.

Fee example:

```text
Debit   Customer Wallet Liability     NGN 101,000
Credit  Settlement/Clearing Asset     NGN 100,000
Credit  Fee Revenue                   NGN 1,000
```

Reversal creates the exact inverse postings in a new transaction. Original postings are never edited.