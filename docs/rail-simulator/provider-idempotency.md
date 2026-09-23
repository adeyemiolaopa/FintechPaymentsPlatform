# Provider Idempotency

The simulator enforces provider idempotency using `(ClientId, ClientReference)`.

For duplicate submissions with the same financial instruction, the original provider reference and status are returned. For duplicate submissions with a changed financial instruction, the simulator returns `409 rail.idempotency_conflict`.

The request hash includes:

- client reference
- destination bank code
- destination account number
- destination account name
- amount rounded to four decimals
- currency
- narration
