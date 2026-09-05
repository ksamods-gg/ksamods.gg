"use client";

import { use, useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { authClient, useSession } from "@/lib/auth-client";
import { SITE_CONTAINER } from "@/lib/layout";
import { orpc } from "@/lib/orpc-browser";

type ClaimResult = { status: string; repo?: string; reason?: string };

export default function ClaimListingPage({
  params,
}: {
  params: Promise<{ id: string }>;
}) {
  const { id } = use(params);
  const router = useRouter();
  const { data: session, isPending } = useSession();

  const [hasGithub, setHasGithub] = useState<boolean | null>(null);
  const [result, setResult] = useState<ClaimResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!isPending && !session)
      router.replace(`/sign-in?next=/mods/${encodeURIComponent(id)}/claim`);
  }, [isPending, session, router, id]);

  useEffect(() => {
    if (!session) return;
    authClient
      .listAccounts()
      .then(({ data }) =>
        setHasGithub(
          Boolean(
            (data as { providerId: string }[] | undefined)?.some(
              (a) => a.providerId === "github",
            ),
          ),
        ),
      )
      .catch(() => setHasGithub(false));
  }, [session]);

  if (isPending || !session) return null;

  function connectGithub() {
    void authClient.linkSocial({
      provider: "github",
      // Return to this same page so the round trip resumes where it left off.
      callbackURL: `${window.location.origin}/mods/${encodeURIComponent(id)}/claim`,
    });
  }

  async function verify() {
    setBusy(true);
    setError(null);
    try {
      setResult(await orpc.listings.claim({ listingId: id }));
    } catch (caught) {
      setError(
        caught instanceof Error ? caught.message : "Verification could not run",
      );
    } finally {
      setBusy(false);
    }
  }

  const steps = [
    { label: "Sign in", done: true },
    { label: "Connect your GitHub account", done: hasGithub === true },
    { label: "Verify push access", done: result?.status === "approved" },
  ];

  return (
    <main className={`${SITE_CONTAINER} flex-1 py-10`}>
      <div className="mx-auto max-w-xl space-y-6">
        <div className="space-y-2">
          <h1 className="text-3xl font-semibold tracking-tight">
            Claim this listing
          </h1>
          <p className="text-muted-foreground">
            We check that your GitHub account has push access to the repository
            this listing declares. Listing id <code>{id}</code>.
          </p>
        </div>

        <Card>
          <CardHeader>
            <CardTitle>Verification steps</CardTitle>
            <CardDescription>
              Maintainership is proven through GitHub, not through the author
              names in the index.
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <ol className="space-y-3">
              {steps.map((step, index) => (
                <li
                  key={step.label}
                  className="flex items-center justify-between gap-3"
                >
                  <span
                    className={
                      step.done ? "text-muted-foreground text-sm" : "text-sm"
                    }
                  >
                    {index + 1}. {step.label}
                  </span>
                  {step.done && <Badge variant="secondary">Done</Badge>}
                </li>
              ))}
            </ol>

            {hasGithub === false && (
              <Button className="w-full" onClick={connectGithub}>
                Connect GitHub
              </Button>
            )}

            {hasGithub === true && (
              <Button className="w-full" onClick={verify} disabled={busy}>
                {busy ? "Checking GitHub..." : "Verify push access"}
              </Button>
            )}

            {error && (
              <p role="alert" className="text-destructive text-sm">
                {error}
              </p>
            )}

            {result?.status === "approved" && (
              <p role="status" className="text-sm">
                Verified. You are now listed as a maintainer of this mod.
              </p>
            )}

            {result?.status === "rejected" && (
              <div role="alert" className="space-y-2">
                <p className="text-destructive text-sm">
                  Your GitHub account does not have push access to {result.repo}
                  .
                </p>
                <p className="text-muted-foreground text-sm">
                  If the repository is private, reconnect GitHub to grant
                  access, or ask an admin to review this claim.
                </p>
                <Button variant="outline" size="sm" onClick={connectGithub}>
                  Reconnect GitHub
                </Button>
              </div>
            )}

            {result?.status === "pending" && (
              <p role="status" className="text-muted-foreground text-sm">
                {result.reason ?? "This claim needs an admin to review it."}
              </p>
            )}
          </CardContent>
        </Card>

        <Button
          render={<Link href={`/mods/${encodeURIComponent(id)}`} />}
          nativeButton={false}
          variant="ghost"
          size="sm"
        >
          Back to the listing
        </Button>
      </div>
    </main>
  );
}
