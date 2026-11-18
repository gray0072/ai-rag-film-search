/**
 * Full page navigation to another origin or a server-rendered page (the payment page).
 * A separate module, so tests can replace it with `vi.mock`.
 */
export function redirectTo(url: string): void {
  window.location.assign(url);
}

/** A new idempotency key for a request that must not be applied twice (spec 04, `Idempotency-Key`). */
export function newIdempotencyKey(): string {
  return crypto.randomUUID();
}
