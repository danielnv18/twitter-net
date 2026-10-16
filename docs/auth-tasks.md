# Backend authentication checklist

## Implemented

- [x] Add IdentityCore with a name, unique normalized username, and unique normalized email.
- [x] Validate trimmed account inputs and bounded, untrimmed passphrases at the API boundary.
- [x] Store PBKDF2 password hashes and SHA-256 session hashes, without plaintext credentials.
- [x] Create the account and initial session atomically and return cookies after commit.
- [x] Support username or email sign-in with generic failures, account lockout, and bounded rate limits.
- [x] Add JSON account, session bootstrap, signup, login, and logout endpoints.
- [x] Enforce backend authorization and CSRF validation on every authentication mutation.
- [x] Rotate the current session at sign-in and revoke it before clearing the logout cookie.
- [x] Check authoritative sessions and security stamps on each authenticated request.
- [x] Enforce 30-minute idle and eight-hour absolute deadlines without absolute renewal.
- [x] Clean expired sessions in bounded background batches.
- [x] Persist Data Protection keys in MySQL with a stable application name.
- [x] Add optional certificate encryption for Data Protection keys.
- [x] Require HTTPS by default outside Development, with an explicit local HTTP opt-in.
- [x] Use secure host-prefixed cookies in HTTPS mode, with HttpOnly, SameSite=Lax, and no Domain.
- [x] Restrict forwarded headers to explicit proxy addresses and one forwarding hop.
- [x] Proxy browser API calls through Vite and Nginx on the same origin.
- [x] Add CSP and other browser security headers to the compiled SPA.
- [x] Add real-MySQL integration tests through a directly referenced API test project.
- [x] Apply development migrations before `make dev` starts the host apps.
- [x] Keep production migrations explicit and preserve separate database volumes.

## Deployment and future account work

- [ ] Provision and renew trusted public TLS certificates before public access.
- [ ] Configure certificate or managed-key encryption of database Data Protection keys in the deployment.
- [ ] Replace local database defaults with deployment secrets and test backup restoration.
- [ ] Add email verification before relying on email ownership.
- [ ] Add password recovery, password changes, and session-management UI.
- [ ] Add MFA if the application requires it.
- [ ] Use distributed rate limiting before running multiple API replicas.
- [ ] Set retention and monitoring policies for account abuse and database failures.

## Dependency maintenance

- [ ] Resolve existing generator and lint-tool dependency advisories when compatible upstream updates are available. The full npm audit reports seven high and five moderate advisories. The production-only audit reports four moderate advisories in the Stylus chain. Non-forced fixes propose no version updates. The compiled Nginx image does not run this Node toolchain.

Development browser flows and Docker HTTPS flows are verified separately from the API integration suite. Deployment tasks above still require deployment-specific configuration.
