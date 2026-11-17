import i18n from "i18next";
import LanguageDetector from "i18next-browser-languagedetector";
import { initReactI18next } from "react-i18next";
import {
  defaultLanguage,
  languageStorageKey,
  supportedLanguages,
  toSupportedLanguage,
  type SupportedLanguage,
} from "./languages";
import en from "./locales/en.json";
import ru from "./locales/ru.json";
import sv from "./locales/sv.json";

export const resources = {
  en: { translation: en },
  ru: { translation: ru },
  sv: { translation: sv },
} as const;

// Resources are bundled (three small JSON files), so initialization is synchronous:
// the first render already has texts and no Suspense fallback is needed.
void i18n
  .use(LanguageDetector)
  .use(initReactI18next)
  .init({
    resources,
    supportedLngs: supportedLanguages,
    fallbackLng: defaultLanguage,
    initAsync: false,
    interpolation: {
      // React escapes output already.
      escapeValue: false,
    },
    detection: {
      // The user's choice first, then the browser language; the choice is saved to localStorage.
      order: ["localStorage", "navigator"],
      lookupLocalStorage: languageStorageKey,
      caches: ["localStorage"],
      convertDetectedLanguage: toSupportedLanguage,
    },
  });

const syncDocumentLanguage = (language: string) => {
  document.documentElement.lang = toSupportedLanguage(language);
};
syncDocumentLanguage(i18n.language);
i18n.on("languageChanged", syncDocumentLanguage);

/** The active UI language; also sent to the API as `Accept-Language`. */
export function currentLanguage(): SupportedLanguage {
  return toSupportedLanguage(i18n.resolvedLanguage ?? i18n.language);
}

export default i18n;
