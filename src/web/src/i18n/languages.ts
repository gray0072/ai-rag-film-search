export const supportedLanguages = ["en", "ru", "sv"] as const;

export type SupportedLanguage = (typeof supportedLanguages)[number];

export const defaultLanguage: SupportedLanguage = "en";

/** localStorage key for the language the user picked. */
export const languageStorageKey = "filmsearch.language";

/**
 * Language names in the language itself (endonyms). They are the same in every UI locale on purpose,
 * so they are not translated through the resource files.
 */
export const languageNames: Record<SupportedLanguage, string> = {
  en: "English",
  ru: "Русский",
  sv: "Svenska",
};

export function isSupportedLanguage(value: string | undefined): value is SupportedLanguage {
  return supportedLanguages.some((language) => language === value);
}

/** Normalizes a language tag (`ru-RU`, `sv`) to a supported language, falling back to English. */
export function toSupportedLanguage(value: string | undefined): SupportedLanguage {
  const base = value?.split("-")[0]?.toLowerCase();
  return isSupportedLanguage(base) ? base : defaultLanguage;
}
