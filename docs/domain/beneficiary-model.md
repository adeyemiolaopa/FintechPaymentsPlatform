# Beneficiary Model

Beneficiaries are customer-owned saved payout destinations. Week 3 supports bank-account style beneficiary records with name, bank code, account number, currency, country code, optional nickname, and status.

Beneficiaries are soft removed by moving status to `Removed` and setting `RemovedAtUtc`. A unique active-beneficiary index prevents duplicate active destinations for the same customer, bank code, account number, and currency.

Self-service users can create, list, and remove their own beneficiaries through `beneficiary.create`, `beneficiary.read.self`, and `beneficiary.remove` permissions. Beneficiary lifecycle changes write audit events and outbox messages.