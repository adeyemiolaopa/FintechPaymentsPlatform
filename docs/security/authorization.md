# Authorization

Authentication proves the caller identity. Roles are broad groupings such as Customer, Operations, Administrator, and Support. Permissions are fine-grained claims such as `customer.read.self`, `customer.suspend`, and `identity.user.manage`.

API endpoints use declarative permission policies. Application services still enforce ownership by deriving self-service customer identity from `ICurrentUser`; callers cannot pass another `customerId` to `/customers/me`.

Account permissions include `account.read.self`, `account.read.any`, `account.create`, `account.freeze`, `account.unfreeze`, `account.close`, `account.restrict`, and `account.unrestrict`. Beneficiary self-service uses `beneficiary.read.self`, `beneficiary.create`, and `beneficiary.remove`.

Ledger permissions are privileged: `ledger.account.create`, `ledger.account.read`, `ledger.transaction.post`, `ledger.transaction.read`, `ledger.transaction.reverse`, `ledger.integrity.read`, and `ledger.audit.read`. Customer-facing APIs must not expose arbitrary ledger posting.


Payment permissions are `payment.create`, `payment.read.self`, `payment.read.any`, `payment.cancel.self`, and `payment.cancel.any`. Self-service customers can create, read, and cancel their own cancellable payments. Operations and administrators can read or cancel across customers where policy allows.