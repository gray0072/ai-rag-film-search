import { LogIn, LogOut, User } from "lucide-react";
import { useTranslation } from "react-i18next";
import { Link, useLocation, useNavigate } from "react-router";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { loginUrl, useCurrentUser, useLogout } from "@/features/auth/auth";

/** "Sign in" for anonymous visitors; the account menu (email, library, sign out) for users (I17). */
export function UserMenu() {
  const { t } = useTranslation();
  const location = useLocation();
  const navigate = useNavigate();
  const user = useCurrentUser();
  const logout = useLogout();

  if (!user.data?.authenticated) {
    const onAuthPage = location.pathname === "/login" || location.pathname === "/register";
    return (
      <Button asChild variant="ghost" size="sm">
        <Link to={onAuthPage ? "/login" : loginUrl(location.pathname + location.search)}>
          <LogIn aria-hidden />
          {t("auth.login.link")}
        </Link>
      </Button>
    );
  }

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" size="icon" aria-label={t("auth.menu", { email: user.data.email })}>
          <User aria-hidden />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <DropdownMenuLabel className="max-w-64 truncate font-normal text-muted-foreground">
          {user.data.email}
        </DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuItem asChild>
          <Link to="/library">{t("nav.library")}</Link>
        </DropdownMenuItem>
        <DropdownMenuItem
          disabled={logout.isPending}
          onSelect={() => {
            logout.mutate({}, { onSuccess: () => void navigate("/") });
          }}
        >
          <LogOut aria-hidden />
          {t("auth.logout")}
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
