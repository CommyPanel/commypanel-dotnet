# Test-only certificates

These files exist only for the TLS tests. They protect nothing: the private keys are committed on purpose, the
certificates are for `localhost`/`127.0.0.1`/`::1` only, and the keys of the two test authorities were thrown away
after signing. Do not trust them anywhere. `generate.sh` makes them again (needs `openssl`).

| File | What it is |
|---|---|
| `ca.pem` | the test authority the client is told to trust |
| `server.pem` / `server.key` | a server certificate from `ca.pem` for localhost, 127.0.0.1 and ::1 |
| `wrong-host.pem` / `wrong-host.key` | from `ca.pem`, but only for `wrong.example`: the name check must refuse it |
| `other-ca.pem` | a second authority the client is not told about |
| `other-server.pem` / `other-server.key` | from `other-ca.pem` for localhost: an untrusted certificate that must be refused |
