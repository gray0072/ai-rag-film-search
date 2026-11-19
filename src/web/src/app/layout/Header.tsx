import { Clapperboard, Search, ShoppingCart } from "lucide-react";
import { useState, type SubmitEvent } from "react";
import { useTranslation } from "react-i18next";
import { Link, NavLink, useMatch, useNavigate, useSearchParams } from "react-router";
import { Button } from "@/components/ui/button";
import { isAdmin } from "@/features/admin/admin";
import { useCurrentUser } from "@/features/auth/auth";
import { useCart } from "@/features/cart/cart";
import { ChatDrawer } from "@/features/chat/ChatDrawer";
import { Input } from "@/components/ui/input";
import { LanguageSwitcher } from "./LanguageSwitcher";
import { UserMenu } from "./UserMenu";

export function Header() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const [query, setQuery] = useState(searchParams.get("q") ?? "");
  // The chat page has the assistant already; elsewhere it opens in a drawer.
  const onChatPage = useMatch("/chat/*") !== null;
  const user = useCurrentUser();

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
          {isAdmin(user.data) && (
            <Button asChild variant="ghost" size="sm">
              <NavLink to="/admin">{t("nav.admin")}</NavLink>
            </Button>
          )}
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
          {!onChatPage && <ChatDrawer />}
          <LanguageSwitcher />
          <CartLink />
          <UserMenu />
        </div>
      </div>
    </header>
  );
}

/** The cart icon with the number of lines; the count is part of the link name for screen readers. */
function CartLink() {
  const { t } = useTranslation();
  const cart = useCart();
  const count = cart.data?.items.length ?? 0;

  return (
    <Button asChild variant="ghost" size="icon" className="relative">
      <Link
        to="/cart"
        aria-label={count > 0 ? t("nav.cartWithItems", { items: count }) : t("nav.cart")}
      >
        <ShoppingCart aria-hidden />
        {count > 0 && (
          <span
            aria-hidden
            className="absolute -top-1 -right-1 flex size-4 items-center justify-center rounded-full bg-primary text-[10px] font-medium text-primary-foreground"
          >
            {count}
          </span>
        )}
      </Link>
    </Button>
  );
}
