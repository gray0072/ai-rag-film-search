import { Clapperboard, MessageCircle, Search, ShoppingCart } from "lucide-react";
import { useState, type SubmitEvent } from "react";
import { useTranslation } from "react-i18next";
import { Link, NavLink, useNavigate, useSearchParams } from "react-router";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { LanguageSwitcher } from "./LanguageSwitcher";

export function Header() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const [query, setQuery] = useState(searchParams.get("q") ?? "");

  const onSubmit = (event: SubmitEvent<HTMLFormElement>) => {
    event.preventDefault();
    const q = query.trim();
    void navigate(q ? `/search?${new URLSearchParams({ q }).toString()}` : "/search");
  };

  return (
    <header className="border-b">
      <div className="mx-auto flex max-w-6xl flex-wrap items-center gap-x-4 gap-y-2 px-4 py-3">
        <Link to="/" className="flex items-center gap-2 text-lg font-semibold">
          <Clapperboard className="size-5" aria-hidden />
          {t("app.name")}
        </Link>

        <nav aria-label={t("nav.main")} className="flex items-center gap-1 text-sm">
          <Button asChild variant="ghost" size="sm">
            <NavLink to="/" end>
              {t("nav.catalog")}
            </NavLink>
          </Button>
          <Button asChild variant="ghost" size="sm">
            <NavLink to="/library">{t("nav.library")}</NavLink>
          </Button>
        </nav>

        <form
          role="search"
          onSubmit={onSubmit}
          className="order-last flex w-full gap-1 md:order-none md:w-auto md:flex-1"
        >
          <Input
            type="search"
            name="q"
            value={query}
            onChange={(event) => {
              setQuery(event.target.value);
            }}
            placeholder={t("search.placeholder")}
            aria-label={t("search.label")}
          />
          <Button type="submit" variant="outline" size="icon" aria-label={t("search.submit")}>
            <Search aria-hidden />
          </Button>
        </form>

        <div className="ml-auto flex items-center gap-1">
          <Button asChild variant="ghost" size="sm">
            <Link to="/chat">
              <MessageCircle aria-hidden />
              <span className="hidden sm:inline">{t("nav.chat")}</span>
              <span className="sr-only sm:hidden">{t("nav.chat")}</span>
            </Link>
          </Button>
          <LanguageSwitcher />
          <Button asChild variant="ghost" size="icon">
            <Link to="/cart" aria-label={t("nav.cart")}>
              <ShoppingCart aria-hidden />
            </Link>
          </Button>
        </div>
      </div>
    </header>
  );
}
