# Authentication

Passwords are never stored or returned. Local implementation uses PBKDF2-SHA256 with per-password random salts and configurable iterations. The default iteration count is 210,000.

Login responses are generic for missing users and wrong passwords to reduce enumeration. Failed logins are counted on the user record; five failures inside the configured window temporarily move the user to `Locked`.

Access tokens are short-lived JWTs. Refresh tokens are cryptographically random, stored as SHA-256 hashes, rotated on every refresh, and revoked on logout.
