# Security Policy

## Reporting a Vulnerability

Please report suspected security issues privately to **adam.coulter@me.com**.
Include enough detail to reproduce the issue (affected version or commit,
schema or input files, and observed behavior). You will receive an
acknowledgement, and fixes will be coordinated before public disclosure
where practical.

Please do not open public issues for security-sensitive reports.

## Schema Files Are Trusted Input

Meridian's merge and diff behavior is driven by schema files (`*.meridian.yaml`)
and by the documents those schemas compose through `includes`/`references`.
Treat schemas — including their transitive remote includes — as trusted code:

- When Meridian is wired in as a Git merge or diff driver, schema loading runs
  automatically during ordinary `git merge` and `git diff` operations.
- Remote `includes`/`references` cause outbound HTTP/HTTPS requests as a side
  effect. A schema can direct Meridian to request arbitrary URLs, which on a
  host with access to internal services or a cloud instance-metadata endpoint
  is a server-side request forgery (SSRF) surface. Meridian does not restrict
  remote includes to an allowlist and does not block private, loopback, or
  link-local addresses.
- Prefer commit-SHA-pinned remote URLs over branch URLs, and keep includes
  local when remote composition is not required.

See the "Security Considerations For Remote Schemas" section of the README for
more detail.
