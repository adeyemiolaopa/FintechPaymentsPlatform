# Manual resolution

Operators may view, assign, request provider requery, verify Ledger evidence, mark resolved, or ignore an exception with a reason code and comment. Every view and transition is audited with actor and UTC timestamp. A resolution records the investigation outcome; it must not be interpreted as permission to set Payment state or edit a balance. Any money movement belongs to an approved Ledger adjustment/reversal workflow and should be re-reconciled after completion.

For critical conflicts, use maker-checker approval before any future financial command. Separate file-upload permission from exception-resolution permission. Keep append-only evidence, and preserve the downstream command identifier in audit. Until maker-checker and an authorized financial-adjustment workflow are implemented, high-risk exceptions should stay open or investigating rather than being closed on weak evidence.

```text
Exception -> operator review -> authorized owning-service command -> fresh evidence -> resolution audit
```
