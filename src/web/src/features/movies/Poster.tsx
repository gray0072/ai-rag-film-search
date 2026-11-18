import { Film } from "lucide-react";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { cn } from "@/lib/utils";
import { posterUrl, type PosterSize } from "@/lib/tmdb";

interface PosterProps {
  posterPath: string | null | undefined;
  title: string;
  size?: PosterSize;
  className?: string;
}

/** A 2:3 poster from the TMDB image CDN, or a neutral placeholder when there is none (or it fails to load). */
export function Poster({ posterPath, title, size = "w342", className }: PosterProps) {
  const { t } = useTranslation();
  const [failed, setFailed] = useState(false);
  const src = posterUrl(posterPath, size);

  return (
    <div className={cn("aspect-[2/3] overflow-hidden rounded-md bg-muted", className)}>
      {src && !failed ? (
        <img
          src={src}
          alt={t("movie.posterAlt", { title })}
          loading="lazy"
          className="size-full object-cover"
          onError={() => {
            setFailed(true);
          }}
        />
      ) : (
        <div
          role="img"
          aria-label={t("movie.noPoster", { title })}
          className="flex size-full items-center justify-center text-muted-foreground"
        >
          <Film className="size-8" aria-hidden />
        </div>
      )}
    </div>
  );
}
