"use client";

import { useEffect, useState } from "react";
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
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { authClient, signOut, useSession } from "@/lib/auth-client";
import { SITE_CONTAINER } from "@/lib/layout";

type Account = {
  id: string;
  providerId: string;
  accountId: string;
  createdAt: Date | string;
  scopes: string[];
};

const PROVIDERS = [
  { id: "discord", label: "Discord" },
  { id: "github", label: "GitHub" },
  { id: "steam", label: "Steam" },
];

export default function AccountPage() {
  const router = useRouter();
  const { data: session, isPending } = useSession();

  const [accounts, setAccounts] = useState<Account[] | null>(null);
  const [name, setName] = useState("");
  const [status, setStatus] = useState<{
    kind: "error" | "ok";
    text: string;
  } | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  useEffect(() => {
    if (!isPending && !session) router.replace("/sign-in");
  }, [isPending, session, router]);

  useEffect(() => {
    if (session) setName(session.user.name);
  }, [session]);

  async function loadAccounts() {
    const { data } = await authClient.listAccounts();
    setAccounts((data as Account[] | undefined) ?? []);
  }

  useEffect(() => {
    if (session) void loadAccounts();
  }, [session]);

  if (isPending || !session) return null;

  const { user } = session;
  // Only providers with a stored account count as connected. The credential
  // row is the email and password login, which is managed separately below.
  const connected = new Map(
    (accounts ?? []).map((account) => [account.providerId, account]),
  );
  const hasPassword = connected.has("credential");
  const canUnlink = (accounts ?? []).length > 1;

  async function saveName() {
    setBusy("name");
    setStatus(null);
    const result = await authClient.updateUser({ name });
    setBusy(null);
    setStatus(
      result.error
        ? {
            kind: "error",
            text: result.error.message ?? "Could not save your name",
          }
        : { kind: "ok", text: "Name updated" },
    );
  }

  function connect(providerId: string) {
    const callbackURL = `${window.location.origin}/account`;
    if (providerId === "steam") {
      void authClient.steam.link({
        callbackURL,
        errorCallbackURL: callbackURL,
      });
      return;
    }
    void authClient.linkSocial({
      provider: providerId as "discord" | "github",
      callbackURL,
    });
  }

  async function disconnect(account: Account) {
    setBusy(account.id);
    setStatus(null);
    const result = await authClient.unlinkAccount({ accountId: account.id });
    setBusy(null);

    if (result.error) {
      setStatus({
        kind: "error",
        text: result.error.message ?? "Could not disconnect",
      });
      return;
    }
    setStatus({ kind: "ok", text: "Disconnected" });
    await loadAccounts();
  }

  return (
    <main className={`${SITE_CONTAINER} flex-1 py-10`}>
      {/* Centred rather than pinned left: three narrow cards against the full
          shell width left the page badly lopsided. */}
      <div className="mx-auto max-w-2xl space-y-6">
        <div className="space-y-2">
          <h1 className="text-3xl font-semibold tracking-tight">Account</h1>
          <p className="text-muted-foreground">
            Manage your profile and sign in methods.
          </p>
        </div>

        {status && (
          <p
            role="status"
            className={
              status.kind === "error" ? "text-destructive text-sm" : "text-sm"
            }
          >
            {status.text}
          </p>
        )}

        <Card>
          <CardHeader>
            <CardTitle>Profile</CardTitle>
            <CardDescription>
              Your display name is shown next to anything you post.
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="space-y-2">
              <Label htmlFor="name">Display name</Label>
              <div className="flex gap-2">
                <Input
                  id="name"
                  value={name}
                  onChange={(event) => setName(event.target.value)}
                />
                <Button
                  onClick={saveName}
                  disabled={
                    busy === "name" || !name.trim() || name === user.name
                  }
                >
                  Save
                </Button>
              </div>
            </div>

            <div className="space-y-2">
              <Label>Email</Label>
              <div className="flex items-center gap-2">
                {/* readOnly, not disabled: disabled dims the value to near
                    unreadable and drops it out of the tab order. */}
                <Input value={user.email} readOnly />
                <Badge variant={user.emailVerified ? "secondary" : "outline"}>
                  {user.emailVerified ? "Verified" : "Unverified"}
                </Badge>
              </div>
            </div>

            <p className="text-muted-foreground text-sm">
              Member since {new Date(user.createdAt).toISOString().slice(0, 10)}
            </p>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Sign in methods</CardTitle>
            <CardDescription>
              Connect a provider to sign in with it. You cannot remove your last
              method.
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            {accounts === null ? (
              <p className="text-muted-foreground text-sm">Loading...</p>
            ) : (
              <>
                <div className="border-border flex items-center justify-between rounded-lg border p-3">
                  <div>
                    <div className="text-sm font-medium">
                      Email and password
                    </div>
                    <div className="text-muted-foreground text-sm">
                      {hasPassword ? "Enabled" : "Not set up"}
                    </div>
                  </div>
                  {hasPassword && <Badge variant="secondary">Connected</Badge>}
                </div>

                {PROVIDERS.map((provider) => {
                  const account = connected.get(provider.id);
                  return (
                    <div
                      key={provider.id}
                      className="border-border flex items-center justify-between rounded-lg border p-3"
                    >
                      <div>
                        <div className="text-sm font-medium">
                          {provider.label}
                        </div>
                        <div className="text-muted-foreground text-sm">
                          {account
                            ? `Connected ${new Date(account.createdAt).toISOString().slice(0, 10)}`
                            : "Not connected"}
                        </div>
                      </div>

                      {account ? (
                        <Button
                          variant="outline"
                          size="sm"
                          disabled={busy === account.id || !canUnlink}
                          onClick={() => disconnect(account)}
                        >
                          Disconnect
                        </Button>
                      ) : (
                        <Button
                          variant="outline"
                          size="sm"
                          onClick={() => connect(provider.id)}
                        >
                          Connect
                        </Button>
                      )}
                    </div>
                  );
                })}
              </>
            )}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Session</CardTitle>
            <CardDescription>Sign out of this browser.</CardDescription>
          </CardHeader>
          <CardContent>
            <Button
              variant="outline"
              onClick={() => signOut().then(() => router.push("/"))}
            >
              Sign out
            </Button>
          </CardContent>
        </Card>
      </div>
    </main>
  );
}
