import { Boxes, LogIn } from "lucide-react";
import type { Metadata } from "next";
import { Suspense } from "react";

import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { signInWithKeycloak } from "@/lib/auth/actions";

export const metadata: Metadata = { title: "Sign in · Resource Provisioner" };

// Auth.js error codes (https://authjs.dev/reference/core/errors) that mean something to the user.
const errorMessages: Record<string, string> = {
  Configuration:
    "Sign-in isn't set up correctly on the server. Ask an admin to check the Keycloak settings.",
  AccessDenied: "Your account isn't allowed to use this app.",
};

export default function SignInPage({ searchParams }: PageProps<"/sign-in">) {
  return (
    <div className="flex min-h-svh items-center justify-center p-4">
      <Card className="w-full max-w-sm">
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <Boxes className="size-5" />
            Resource Provisioner
          </CardTitle>
          <CardDescription>
            Sign in with your account. Access is by invitation only.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <Suspense fallback={<SignInButton disabled />}>
            <SignInForm searchParams={searchParams} />
          </Suspense>
        </CardContent>
      </Card>
    </div>
  );
}

async function SignInForm({
  searchParams,
}: Pick<PageProps<"/sign-in">, "searchParams">) {
  const { callbackUrl, error } = await searchParams;
  const signIn = signInWithKeycloak.bind(
    null,
    typeof callbackUrl === "string" ? callbackUrl : undefined,
  );
  const errorMessage =
    typeof error === "string"
      ? (errorMessages[error] ?? "Signing in didn't work. Please try again.")
      : null;

  return (
    <form action={signIn} className="flex flex-col gap-4">
      {errorMessage && (
        <p role="alert" className="text-sm text-destructive">
          {errorMessage}
        </p>
      )}
      <SignInButton />
    </form>
  );
}

function SignInButton({ disabled = false }: { disabled?: boolean }) {
  return (
    <Button type="submit" className="w-full" disabled={disabled}>
      <LogIn /> Sign in
    </Button>
  );
}
