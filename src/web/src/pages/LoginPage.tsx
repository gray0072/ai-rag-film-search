import { AuthForm } from "@/features/auth/AuthForm";

/** FR-USR-2: sign in; the anonymous cart, orders and conversations move to the account. */
export function LoginPage() {
  return <AuthForm mode="login" />;
}
