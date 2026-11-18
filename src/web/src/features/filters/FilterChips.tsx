import { Sparkles, X } from "lucide-react";
import { useTranslation } from "react-i18next";
import { currentLanguage } from "@/i18n";
import { formatMoney, languageName } from "@/lib/format";
import { chipKey, type FilterChip } from "./filters";
import { useGenres } from "./useGenres";

interface FilterChipsProps {
  chips: FilterChip[];
  /** Chips found by query understanding (FR-SRCH-4) get a mark. */
  isUnderstood?: (chip: FilterChip) => boolean;
  onRemove: (chip: FilterChip) => void;
}

export function FilterChips({ chips, isUnderstood, onRemove }: FilterChipsProps) {
  const { t } = useTranslation();
  const label = useChipLabel();

  if (chips.length === 0) {
    return null;
  }

  return (
    <ul aria-label={t("filters.applied")} className="flex flex-wrap gap-2">
      {chips.map((chip) => {
        const text = label(chip);
        const understood = isUnderstood?.(chip) ?? false;
        return (
          <li
            key={chipKey(chip)}
            className="inline-flex items-center gap-1 rounded-full border bg-secondary py-0.5 pr-1 pl-2.5 text-sm text-secondary-foreground"
          >
            {understood && (
              <Sparkles
                className="size-3.5 text-muted-foreground"
                aria-label={t("filters.understood")}
                role="img"
              />
            )}
            <span>{text}</span>
            <button
              type="button"
              onClick={() => {
                onRemove(chip);
              }}
              aria-label={t("filters.removeChip", { filter: text })}
              className="rounded-full p-0.5 text-muted-foreground outline-none hover:bg-background hover:text-foreground focus-visible:ring-[3px] focus-visible:ring-ring/50"
            >
              <X className="size-3.5" aria-hidden />
            </button>
          </li>
        );
      })}
    </ul>
  );
}

function useChipLabel() {
  const { t } = useTranslation();
  const genres = useGenres();
  const language = currentLanguage();
  const genreName = (slug: string) =>
    genres.data?.find((genre) => genre.slug === slug)?.name ?? slug;
  const number = (value: number) => new Intl.NumberFormat(language).format(value);

  return (chip: FilterChip): string => {
    switch (chip.kind) {
      case "genre":
        return genreName(chip.slug);
      case "excludeGenre":
        return t("filters.chip.excludeGenre", { genre: genreName(chip.slug) });
      case "yearFrom":
        return t("filters.chip.yearFrom", { year: chip.value });
      case "yearTo":
        return t("filters.chip.yearTo", { year: chip.value });
      case "minRating":
        return t("filters.chip.minRating", { rating: number(chip.value) });
      case "runtimeMin":
        return t("filters.chip.runtimeMin", { minutes: chip.value });
      case "runtimeMax":
        return t("filters.chip.runtimeMax", { minutes: chip.value });
      case "priceMax":
        // Prices of the catalog are in USD (seed); a multi-currency catalog would need the currency here.
        return t("filters.chip.priceMax", { price: formatMoney(chip.value, "USD", language) });
      case "language":
        return t("filters.chip.language", { language: languageName(chip.value, language) });
      case "offerType":
        return chip.value === "buy" ? t("offer.buy") : t("offer.rent");
    }
  };
}
