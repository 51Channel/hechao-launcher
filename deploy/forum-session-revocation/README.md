# Forum session revocation overlay

This overlay adds the local-only authenticated endpoint used by the launcher API
to invalidate previously issued forum cookies.

The API writes every revocation intent to PostgreSQL in the same transaction as
the administrator security action. A background worker retries delivery with a
stable request ID. The forum stores that request ID before incrementing
`User.sessionVersion`, so retries are idempotent.

The overlay intentionally contains only the new route and Prisma migration. It
does not duplicate or replace unrelated forum source files.
