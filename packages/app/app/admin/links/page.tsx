"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { Badge } from "@/components/ui/badge";
import { orpc } from "@/lib/orpc-browser";

type LinkIssue = {
  kind: string;
  listingId: string;
  claimedModId: string | null;
  boundTo?: string;
  detail: string;
};

// Only a contested claim is hidden. An orphaned row is our own bookkeeping.
const HIDES_LISTING: Record<string, boolean> = {
  "unknown-id": true,
  "wrong-listing": true,
  orphaned: false,
};

export default function AdminLinksPage() {
  const [issues, setIssues] = useState<LinkIssue[] | null>(null);

  useEffect(() => {
    orpc.listings.admin
      .linkIssues()
      .then((rows) => setIssues(rows as LinkIssue[]))
      .catch(() => setIssues([]));
  }, []);

  return (
    <>
      <div className="mb-8 space-y-2">
        <h1 className="text-2xl font-semibold tracking-tight">Link issues</h1>
        <p className="text-muted-foreground text-sm">
          Listings whose <code className="font-mono">ksamods-gg</code> metadata
          does not resolve to the mod row it names. A contested listing is
          hidden from end users until this is resolved; the listing the row is
          actually bound to stays visible.
        </p>
      </div>

      {issues === null ? (
        <p className="text-muted-foreground text-sm">Loading...</p>
      ) : issues.length === 0 ? (
        <div className="border-border text-muted-foreground rounded-lg border border-dashed py-16 text-center text-sm">
          Every listing link resolves.
        </div>
      ) : (
        <ul className="space-y-3">
          {issues.map((issue, index) => (
            <li
              key={`${issue.listingId}-${index}`}
              className="border-border space-y-2 rounded-lg border p-4"
            >
              <div className="flex flex-wrap items-start justify-between gap-3">
                <Link
                  href={`/mods/${encodeURIComponent(issue.listingId)}`}
                  className="font-medium underline-offset-4 hover:underline"
                >
                  {issue.listingId}
                </Link>
                <Badge
                  variant={
                    HIDES_LISTING[issue.kind] ? "destructive" : "secondary"
                  }
                >
                  {HIDES_LISTING[issue.kind] ? "Hidden" : "Visible"}
                </Badge>
              </div>
              <p className="text-muted-foreground text-sm">
                {issue.detail}
                {issue.claimedModId ? ` (${issue.claimedModId})` : ""}
              </p>
              {issue.boundTo && (
                <p className="text-muted-foreground text-sm">
                  The row belongs to{" "}
                  <Link
                    href={`/mods/${encodeURIComponent(issue.boundTo)}`}
                    className="underline underline-offset-4"
                  >
                    {issue.boundTo}
                  </Link>
                  .
                </p>
              )}
            </li>
          ))}
        </ul>
      )}
    </>
  );
}
