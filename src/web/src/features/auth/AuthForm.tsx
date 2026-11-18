import { useState, type SubmitEvent } from "react";
import { useTranslation } from "react-i18next";
import { Link, Navigate, useNavigate, useSearchParams } from "react-router";
import { problemCode, problemStatus } from "@/api/problem";
import { PageTitle } from "@/components/StatusMessages";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { safeReturnUrl, useCurrentUser, useLogin, useRegister } from "./auth";

/** Same limits as the API (FR-USR-2): passwords of 8–128 characters, no composition rules. */
const passwordMinLength = 8;
const passwordMaxLength = 128;

type Mode = "login" | "register";

/** The login and the registration page; both return to `returnUrl` afterwards. */
export function AuthForm({ mode }: { mode: Mode }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const returnUrl = safeReturnUrl(searchParams.get("returnUrl"));
  const user = useCurrentUser();
  const login = useLogin();
  const register = useRegister();
  const mutation = mode === "login" ? login : register;
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");

  if (user.data?.authenticated && !mutation.isSuccess) {
    return <Navigate to={returnUrl} replace />;
  }

  const onSubmit = (event: SubmitEvent<HTMLFormElement>) => {
    event.preventDefault();
    mutation.mutate(
      { body: { email: email.trim(), password } },
      { onSuccess: () => void navigate(returnUrl, { replace: true }) },
    );
  };

  const otherMode = mode === "login" ? "register" : "login";
  const otherLink = `/${otherMode}?${new URLSearchParams({ returnUrl }).toString()}`;

  return (
    <section className="mx-auto flex w-full max-w-sm flex-col gap-6">
      <div className="flex flex-col gap-2">
        <PageTitle>{t(`auth.${mode}.title`)}</PageTitle>
        <p className="text-sm text-muted-foreground">{t(`auth.${mode}.description`)}</p>
      </div>

      <form onSubmit={onSubmit} className="flex flex-col gap-4">
        <div className="flex flex-col gap-2">
          <Label htmlFor="auth-email">{t("auth.email")}</Label>
          <Input
            id="auth-email"
            type="email"
            name="email"
            autoComplete="email"
            required
            maxLength={256}
            value={email}
            onChange={(event) => {
              setEmail(event.target.value);
            }}
          />
        </div>
        <div className="flex flex-col gap-2">
          <Label htmlFor="auth-password">{t("auth.password")}</Label>
          <Input
            id="auth-password"
            type="password"
            name="password"
            autoComplete={mode === "login" ? "current-password" : "new-password"}
            required
            minLength={mode === "register" ? passwordMinLength : undefined}
            maxLength={passwordMaxLength}
            aria-describedby={mode === "register" ? "auth-password-hint" : undefined}
            value={password}
            onChange={(event) => {
              setPassword(event.target.value);
            }}
          />
          {mode === "register" && (
            <p id="auth-password-hint" className="text-xs text-muted-foreground">
              {t("auth.passwordHint", { min: passwordMinLength })}
            </p>
          )}
        </div>

        {mutation.isError && (
          <Alert variant="destructive">
            <AlertDescription>{authErrorMessage(mutation.error, t)}</AlertDescription>
          </Alert>
        )}

        <Button type="submit" disabled={mutation.isPending}>
          {mutation.isPending ? t("auth.submitting") : t(`auth.${mode}.submit`)}
        </Button>
      </form>

      <p className="text-sm text-muted-foreground">
        {t(`auth.${mode}.switchPrompt`)}{" "}
        <Link to={otherLink} className="font-medium text-foreground underline underline-offset-4">
          {t(`auth.${otherMode}.switchLink`)}
        </Link>
      </p>
    </section>
  );
}

type Translate = ReturnType<typeof useTranslation>["t"];

/** The message for a refused login or registration (spec 04 "Auth"). */
function authErrorMessage(error: unknown, t: Translate): string {
  switch (problemCode(error)) {
    case "invalidCredentials":
      return t("auth.errors.invalidCredentials");
    case "lockedOut":
      return t("auth.errors.lockedOut");
    case "emailTaken":
      return t("auth.errors.emailTaken");
  }
  if (problemStatus(error) === 400) {
    const errors =
      typeof error === "object" && error !== null && "errors" in error ? error.errors : null;
    const fields = Object.keys(errors ?? {}).map((field) => field.toLowerCase());
    return fields.includes("password")
      ? t("auth.errors.passwordTooShort", { min: passwordMinLength })
      : t("auth.errors.invalidEmail");
  }
  return t("common.errorGeneric");
}
