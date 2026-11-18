/** Money is stored in minor units (cents, öre); the currency's own number of decimals is used. */
export function formatMoney(minor: number, currency: string, language: string): string {
  const format = new Intl.NumberFormat(language, { style: "currency", currency });
  const digits = format.resolvedOptions().maximumFractionDigits ?? 2;
  return format.format(minor / 10 ** digits);
}

/** Minor units to the major amount shown in a price input (499 → 4.99 for USD). */
export function minorToMajor(minor: number, currency = "USD"): number {
  return minor / 10 ** fractionDigits(currency);
}

/** The major amount typed by the user to minor units, rounded (4.99 → 499 for USD). */
export function majorToMinor(major: number, currency = "USD"): number {
  return Math.round(major * 10 ** fractionDigits(currency));
}

function fractionDigits(currency: string): number {
  return (
    new Intl.NumberFormat("en", { style: "currency", currency }).resolvedOptions()
      .maximumFractionDigits ?? 2
  );
}

export function formatDateTime(iso: string, language: string): string {
  return new Intl.DateTimeFormat(language, { dateStyle: "medium", timeStyle: "short" }).format(
    new Date(iso),
  );
}

export function formatRating(value: number, language: string): string {
  return new Intl.NumberFormat(language, {
    minimumFractionDigits: 1,
    maximumFractionDigits: 1,
  }).format(value);
}

/** The name of an ISO 639-1 language code in the UI language ("ja" → "Japanese" / "японский"). */
export function languageName(code: string, language: string): string {
  try {
    return new Intl.DisplayNames([language], { type: "language" }).of(code) ?? code;
  } catch {
    return code;
  }
}
