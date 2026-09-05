"use client";

import { Suspense, useState } from "react";
import { useSearchParams } from "next/navigation";
import { safeNext } from "@/lib/next-path";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { authClient, signIn, signUp } from "@/lib/auth-client";

function SignInForm() {
  const next = safeNext(useSearchParams().get("next"));
  const [mode, setMode] = useState<"sign-in" | "sign-up">("sign-in");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [name, setName] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  const isSignUp = mode === "sign-up";

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setPending(true);

    const result = isSignUp
      ? await signUp.email({ email, password, name })
      : await signIn.email({ email, password });

    setPending(false);
    if (result.error) setError(result.error.message ?? "Something went wrong");
    else window.location.href = next;
  }

  return (
    <div className="flex flex-1 items-center justify-center px-6 py-16">
      <Card className="w-full max-w-sm">
        <CardHeader>
          <CardTitle>{isSignUp ? "Create an account" : "Sign in"}</CardTitle>
          <CardDescription>
            {isSignUp
              ? "Enter your details to get started."
              : "Welcome back. Sign in to continue."}
          </CardDescription>
        </CardHeader>

        <CardContent className="space-y-6">
          <form onSubmit={onSubmit} className="space-y-4">
            {isSignUp && (
              <div className="space-y-2">
                <Label htmlFor="name">Name</Label>
                <Input
                  id="name"
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  required
                />
              </div>
            )}

            <div className="space-y-2">
              <Label htmlFor="email">Email</Label>
              <Input
                id="email"
                type="email"
                autoComplete="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                required
              />
            </div>

            <div className="space-y-2">
              <Label htmlFor="password">Password</Label>
              <Input
                id="password"
                type="password"
                autoComplete={isSignUp ? "new-password" : "current-password"}
                minLength={8}
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                required
              />
            </div>

            {error && (
              <p role="alert" className="text-destructive text-sm">
                {error}
              </p>
            )}

            <Button type="submit" className="w-full" disabled={pending}>
              {isSignUp ? "Sign up" : "Sign in"}
            </Button>
          </form>

          <div className="relative">
            <div className="absolute inset-0 flex items-center">
              <span className="border-border w-full border-t" />
            </div>
            <div className="relative flex justify-center">
              <span className="bg-card text-muted-foreground px-2 text-xs uppercase">
                Or continue with
              </span>
            </div>
          </div>

          <div className="space-y-2">
            <Button
              variant="outline"
              className="w-full"
              onClick={() =>
                signIn.social({
                  provider: "discord",
                  callbackURL: `${window.location.origin}${next}`,
                })
              }
            >
              Discord
            </Button>
            <Button
              variant="outline"
              className="w-full"
              onClick={() =>
                signIn.social({
                  provider: "github",
                  callbackURL: `${window.location.origin}${next}`,
                })
              }
            >
              GitHub
            </Button>
            <Button
              variant="outline"
              className="w-full"
              onClick={() =>
                authClient.steam.login({
                  callbackURL: `${window.location.origin}${next}`,
                  errorCallbackURL: `${window.location.origin}/sign-in`,
                })
              }
            >
              Steam
            </Button>
          </div>
        </CardContent>

        <CardFooter>
          <Button
            variant="link"
            className="w-full"
            onClick={() => setMode(isSignUp ? "sign-in" : "sign-up")}
          >
            {isSignUp
              ? "Already have an account? Sign in"
              : "Need an account? Sign up"}
          </Button>
        </CardFooter>
      </Card>
    </div>
  );
}

/**
 * useSearchParams opts the tree into client rendering, so it needs a boundary
 * for the page to prerender.
 */
export default function SignIn() {
  return (
    <Suspense>
      <SignInForm />
    </Suspense>
  );
}
