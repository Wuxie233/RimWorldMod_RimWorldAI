export function redactSensitive(value: string): string {
  return value
    .replace(/(sk-[A-Za-z0-9_-]{8})[A-Za-z0-9_-]+/g, '$1...redacted')
    .replace(/(Bearer\s+)[A-Za-z0-9._~+/=-]{8,}/gi, '$1...redacted')
    .replace(/(api[_-]?key["'\s:=]+)[^"'\s,;]+/gi, '$1...redacted')
    .replace(/(authorization["'\s:=]+)[^"'\s,;]+/gi, '$1...redacted');
}

export function safeErrorMessage(err: unknown): string {
  if (err instanceof Error) return redactSensitive(err.message);
  return redactSensitive(String(err));
}

export function safeLogError(prefix: string, err: unknown): void {
  const name = err instanceof Error ? err.name : typeof err;
  const stack = err instanceof Error && err.stack ? redactSensitive(err.stack) : '';
  console.error(`${prefix}: ${safeErrorMessage(err)} name=${name}${stack ? ` stack=${stack}` : ''}`);
}
