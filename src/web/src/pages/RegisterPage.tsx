import { AuthForm } from "@/features/auth/AuthForm";

/** FR-USR-2: create an account with email and password; it is signed in at once. */
export function RegisterPage() {
  return <AuthForm mode="register" />;
}
