import { Languages } from "lucide-react";
import { useId } from "react";
import { useTranslation } from "react-i18next";
import { currentLanguage } from "@/i18n";
import { languageNames, supportedLanguages } from "@/i18n/languages";

export function LanguageSwitcher() {
  const { t, i18n } = useTranslation();
  const id = useId();

  return (
    <div className="flex items-center gap-1">
      <Languages className="size-4 text-muted-foreground" aria-hidden />
      <label htmlFor={id} className="sr-only">
        {t("language.label")}
      </label>
      <select
        id={id}
        value={currentLanguage()}
        onChange={(event) => {
          void i18n.changeLanguage(event.target.value);
        }}
        className="h-8 rounded-md border border-input bg-transparent px-2 text-sm shadow-xs outline-none focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50"
      >
        {supportedLanguages.map((language) => (
          <option key={language} value={language} lang={language}>
            {languageNames[language]}
          </option>
        ))}
      </select>
    </div>
  );
}
