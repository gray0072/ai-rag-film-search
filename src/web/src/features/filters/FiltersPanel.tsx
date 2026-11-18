import { useId, useState, type ReactNode, type SubmitEvent } from "react";
import { useTranslation } from "react-i18next";
import type { GenreItem } from "@/api/types";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { currentLanguage } from "@/i18n";
import { languageName, majorToMinor, minorToMajor } from "@/lib/format";
import { emptyFilters, filterLanguages, type MovieFilters } from "./filters";
import { useGenres } from "./useGenres";

const ratingOptions = [5, 6, 7, 7.5, 8, 8.5];

interface FiltersPanelProps {
  /** The applied filters (from the URL). The panel edits a draft and calls `onApply` on submit. */
  filters: MovieFilters;
  onApply: (filters: MovieFilters) => void;
}

/**
 * The catalog filters of FR-CAT-2 as a form. Mount it with a `key` of the applied filters,
 * so the draft starts again from the URL after back / forward navigation.
 */
export function FiltersPanel({ filters, onApply }: FiltersPanelProps) {
  const { t } = useTranslation();
  const language = currentLanguage();
  const genres = useGenres();
  const [draft, setDraft] = useState(filters);
  const id = useId();

  const update = (patch: Partial<MovieFilters>) => {
    setDraft((current) => ({ ...current, ...patch }));
  };

  const toggleGenre = (slug: string, checked: boolean) => {
    setDraft((current) => ({
      ...current,
      genres: checked
        ? [...current.genres, slug]
        : current.genres.filter((selected) => selected !== slug),
    }));
  };

  const onSubmit = (event: SubmitEvent<HTMLFormElement>) => {
    event.preventDefault();
    onApply(draft);
  };

  const onReset = () => {
    // Excluded genres come from query understanding and are removed with their chips.
    const reset = { ...emptyFilters, excludeGenres: filters.excludeGenres };
    setDraft(reset);
    onApply(reset);
  };

  return (
    <form
      onSubmit={onSubmit}
      aria-label={t("filters.title")}
      className="flex flex-col gap-5 text-sm"
    >
      <fieldset className="flex flex-col gap-2">
        <legend className="mb-2 font-medium">{t("filters.genres")}</legend>
        {genres.data ? (
          <div className="grid grid-cols-2 gap-x-3 gap-y-1.5 md:grid-cols-1">
            {genres.data.map((genre: GenreItem) => {
              const checkboxId = `${id}-genre-${genre.slug}`;
              return (
                <div key={genre.slug} className="flex items-center gap-2">
                  <Checkbox
                    id={checkboxId}
                    checked={draft.genres.includes(genre.slug)}
                    onCheckedChange={(checked) => {
                      toggleGenre(genre.slug, checked === true);
                    }}
                  />
                  <Label htmlFor={checkboxId} className="font-normal">
                    {genre.name}
                  </Label>
                </div>
              );
            })}
          </div>
        ) : (
          <p className="text-muted-foreground">
            {genres.isError ? t("filters.genresUnavailable") : t("common.loading")}
          </p>
        )}
      </fieldset>

      <Range legend={t("filters.year")}>
        <NumberField
          label={t("filters.from")}
          value={draft.yearFrom}
          min={1870}
          max={2100}
          onChange={(yearFrom) => {
            update({ yearFrom });
          }}
        />
        <NumberField
          label={t("filters.to")}
          value={draft.yearTo}
          min={1870}
          max={2100}
          onChange={(yearTo) => {
            update({ yearTo });
          }}
        />
      </Range>

      <SelectField
        label={t("filters.minRating")}
        value={draft.minRating === undefined ? "" : String(draft.minRating)}
        onChange={(value) => {
          update({ minRating: value ? Number(value) : undefined });
        }}
      >
        <option value="">{t("filters.any")}</option>
        {ratingOptions.map((rating) => (
          <option key={rating} value={rating}>
            {t("filters.ratingAtLeast", { rating: new Intl.NumberFormat(language).format(rating) })}
          </option>
        ))}
      </SelectField>

      <Range legend={t("filters.runtime")}>
        <NumberField
          label={t("filters.from")}
          value={draft.runtimeMin}
          min={1}
          max={1000}
          onChange={(runtimeMin) => {
            update({ runtimeMin });
          }}
        />
        <NumberField
          label={t("filters.to")}
          value={draft.runtimeMax}
          min={1}
          max={1000}
          onChange={(runtimeMax) => {
            update({ runtimeMax });
          }}
        />
      </Range>

      <SelectField
        label={t("filters.language")}
        value={draft.language ?? ""}
        onChange={(value) => {
          update({ language: value || undefined });
        }}
      >
        <option value="">{t("filters.any")}</option>
        {filterLanguages.map((code) => (
          <option key={code} value={code}>
            {languageName(code, language)}
          </option>
        ))}
        {draft.language && !filterLanguages.some((code) => code === draft.language) && (
          <option value={draft.language}>{languageName(draft.language, language)}</option>
        )}
      </SelectField>

      <NumberField
        label={t("filters.priceMax")}
        value={draft.priceMax === undefined ? undefined : minorToMajor(draft.priceMax)}
        min={0}
        step={0.01}
        onChange={(price) => {
          update({ priceMax: price === undefined ? undefined : majorToMinor(price) });
        }}
      />

      <SelectField
        label={t("filters.offerType")}
        value={draft.offerType ?? ""}
        onChange={(value) => {
          update({ offerType: value === "buy" || value === "rent" ? value : undefined });
        }}
      >
        <option value="">{t("filters.any")}</option>
        <option value="buy">{t("offer.buy")}</option>
        <option value="rent">{t("offer.rent")}</option>
      </SelectField>

      <div className="flex gap-2">
        <Button type="submit" size="sm">
          {t("filters.apply")}
        </Button>
        <Button type="button" variant="ghost" size="sm" onClick={onReset}>
          {t("filters.reset")}
        </Button>
      </div>
    </form>
  );
}

function Range({ legend, children }: { legend: string; children: ReactNode }) {
  return (
    <fieldset>
      <legend className="mb-2 font-medium">{legend}</legend>
      <div className="grid grid-cols-2 gap-2">{children}</div>
    </fieldset>
  );
}

interface NumberFieldProps {
  label: string;
  value: number | undefined;
  onChange: (value: number | undefined) => void;
  min?: number;
  max?: number;
  step?: number;
}

function NumberField({ label, value, onChange, min, max, step }: NumberFieldProps) {
  const id = useId();
  return (
    <div className="flex flex-col gap-1">
      <Label htmlFor={id} className="text-xs text-muted-foreground">
        {label}
      </Label>
      <Input
        id={id}
        type="number"
        inputMode="decimal"
        value={value ?? ""}
        min={min}
        max={max}
        step={step}
        onChange={(event) => {
          const text = event.target.value;
          const number = Number(text);
          onChange(text === "" || !Number.isFinite(number) ? undefined : number);
        }}
      />
    </div>
  );
}

interface SelectFieldProps {
  label: string;
  value: string;
  onChange: (value: string) => void;
  children: ReactNode;
}

export function SelectField({ label, value, onChange, children }: SelectFieldProps) {
  const id = useId();
  return (
    <div className="flex flex-col gap-1">
      <Label htmlFor={id} className="font-medium">
        {label}
      </Label>
      <select
        id={id}
        value={value}
        onChange={(event) => {
          onChange(event.target.value);
        }}
        className="h-9 rounded-md border border-input bg-transparent px-2 text-sm shadow-xs outline-none focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50"
      >
        {children}
      </select>
    </div>
  );
}
