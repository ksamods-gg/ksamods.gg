"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { SITE_CONTAINER } from "@/lib/layout";
import { orpc } from "@/lib/orpc-browser";

type LinkIssue = {
  kind: string;
  listingId: string;
  claimedModId: string | null;
  boundTo?: string;
  detail: string;
};

type Claim = {
  id: string;
  listingId: string;
  status: string;
  note: string | null;
  decidedById: string | null;
  decidedAt: Date | string | null;
  user: { id: string; name: string; email: string };
};

export default function AdminPage() {
  const [claims, setClaims] = useState<Claim[] | null>(null);
  const [issues, setIssues] = useState<LinkIssue[]>([]);
  const [denied, setDenied] = useState(false);
  const [notes, setNotes] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState<string | null>(null);

  const load = useCallback(() => {
    orpc.listings.admin
      .claims()
      .then((rows) => setClaims(rows as Claim[]))
      // The procedure is the real gate; this only decides what to render.
      .catch(() => setDenied(true));

    orpc.listings.admin
      .linkIssues()
      .then((rows) => setIssues(rows as LinkIssue[]))
      .catch(() => setIssues([]));
  }, []);

  useEffect(load, [load]);

  async function decide(
    claimId: string,
    status: "approved" | "rejected" | "revoked",
  ) {
    setBusy(claimId);
    try {
      await orpc.listings.admin.decide({
        claimId,
        status,
        note: notes[claimId] || undefined,
      });
      load();
    } finally {
      setBusy(null);
    }
  }

  if (denied) {
    return (
      <main className={`${SITE_CONTAINER} flex-1 py-10`}>
        <p className="text-muted-foreground text-sm">
          You do not have access to this page.
        </p>
      </main>
    );
  }

  return (
    <main className={`${SITE_CONTAINER} flex-1 py-10`}>
      <div className="mb-8 space-y-2">
        <h1 className="text-3xl font-semibold tracking-tight">Claims</h1>
        <p className="text-muted-foreground">
          Every listing claim, newest first. A claim with no decider was decided
          by the automated GitHub check.
        </p>
      </div>

      {issues.length > 0 && (
        <div className="border-destructive/40 mb-8 space-y-2 rounded-lg border p-4">
          <h2 className="text-destructive text-sm font-medium">
            {issues.length === 1
              ? "One listing link needs attention"
              : `${issues.length} listing links need attention`}
          </h2>
          <p className="text-muted-foreground text-sm">
            A contested listing is hidden from end users until this is resolved.
            The listing the mod row is bound to stays visible.
          </p>
          <ul className="space-y-1 text-sm">
            {issues.map((issue, index) => (
              <li key={index}>
                <span className="font-medium">{issue.listingId}</span>
                <span className="text-muted-foreground">
                  {" "}
                  {issue.detail}
                  {issue.claimedModId ? ` (${issue.claimedModId})` : ""}
                </span>
              </li>
            ))}
          </ul>
        </div>
      )}

      {claims === null ? (
        <p className="text-muted-foreground text-sm">Loading...</p>
      ) : claims.length === 0 ? (
        <div className="border-border text-muted-foreground rounded-lg border border-dashed py-16 text-center text-sm">
          No claims yet.
        </div>
      ) : (
        <ul className="space-y-3">
          {claims.map((claim) => (
            <li
              key={claim.id}
              className="border-border space-y-3 rounded-lg border p-4"
            >
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="min-w-0 space-y-1">
                  <Link
                    href={`/mods/${encodeURIComponent(claim.listingId)}`}
                    className="font-medium underline-offset-4 hover:underline"
                  >
                    {claim.listingId}
                  </Link>
                  <div className="text-muted-foreground text-sm">
                    {claim.user.name} ({claim.user.email})
                  </div>
                  <div className="text-muted-foreground text-sm">
                    {claim.decidedById
                      ? "Decided by an admin"
                      : "Decided by the GitHub check"}
                    {claim.decidedAt
                      ? ` on ${new Date(claim.decidedAt).toISOString().slice(0, 10)}`
                      : ""}
                  </div>
                  {claim.note && (
                    <div className="text-muted-foreground text-sm">
                      {claim.note}
                    </div>
                  )}
                </div>
                <Badge
                  variant={
                    claim.status === "approved" ? "secondary" : "outline"
                  }
                >
                  {claim.status}
                </Badge>
              </div>

              <div className="flex flex-wrap items-center gap-2">
                <Input
                  className="max-w-xs"
                  placeholder="Reason (shown to the claimant)"
                  value={notes[claim.id] ?? ""}
                  onChange={(event) =>
                    setNotes((current) => ({
                      ...current,
                      [claim.id]: event.target.value,
                    }))
                  }
                />
                <Button
                  size="sm"
                  variant="outline"
                  disabled={busy === claim.id}
                  onClick={() => decide(claim.id, "approved")}
                >
                  Approve
                </Button>
                <Button
                  size="sm"
                  variant="outline"
                  disabled={busy === claim.id}
                  onClick={() => decide(claim.id, "revoked")}
                >
                  Revoke
                </Button>
              </div>
            </li>
          ))}
        </ul>
      )}
    </main>
  );
}
