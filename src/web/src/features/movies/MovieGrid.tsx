import type { ReactNode } from "react";
import { Skeleton } from "@/components/ui/skeleton";
import { cn } from "@/lib/utils";

interface MovieGridProps {
  children: ReactNode;
  label: string;
  /** The previous results stay on screen while the next ones load: dimmed and marked busy. */
  stale?: boolean;
}

/** A responsive grid of movie cards; a list for assistive technology. */
export function MovieGrid({ children, label, stale = false }: MovieGridProps) {
  return (
    <ul
      aria-label={label}
      aria-busy={stale}
      className={cn(
        "grid grid-cols-2 gap-x-4 gap-y-6 transition-opacity sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5",
        stale && "opacity-50",
      )}
    >
      {children}
    </ul>
  );
}

export function MovieGridSkeleton({ count = 10, label }: { count?: number; label: string }) {
  return (
    <div
      role="status"
      aria-label={label}
      className="grid grid-cols-2 gap-x-4 gap-y-6 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5"
    >
      {Array.from({ length: count }, (_, index) => (
        <div key={index} className="flex flex-col gap-2">
          <Skeleton className="aspect-[2/3] w-full" />
          <Skeleton className="h-4 w-3/4" />
          <Skeleton className="h-3 w-1/2" />
        </div>
      ))}
    </div>
  );
}
