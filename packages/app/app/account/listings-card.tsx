"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { orpc } from "@/lib/orpc-browser";

type Claim = {
  id: string;
  listingId: string;
  status: string;
  note: string | null;
  decidedAt: Date | string | null;
};

const STATUS_LABEL: Record<string, string> = {
  approved: "Verified",
  pending: "Awaiting review",
  rejected: "Not verified",
  revoked: "Revoked",
};

export function YourListingsCard() {
  const [claims, setClaims] = useState<Claim[] | null>(null);

  useEffect(() => {
    orpc.listings
      .mine()
      .then((rows) => setClaims(rows as Claim[]))
      .catch(() => setClaims([]));
  }, []);

  return (
    <Card>
      <CardHeader>
        <CardTitle>Your listings</CardTitle>
        <CardDescription>
          Mods you have claimed, verified through GitHub push access.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        {claims === null ? (
          <p className="text-muted-foreground text-sm">Loading...</p>
        ) : claims.length === 0 ? (
          <div className="space-y-3">
            <p className="text-muted-foreground text-sm">
              You have not claimed any listings.
            </p>
            <Button
              render={<Link href="/mods" />}
              nativeButton={false}
              variant="outline"
              size="sm"
            >
              Browse mods
            </Button>
          </div>
        ) : (
          claims.map((claim) => (
            <div
              key={claim.id}
              className="border-border flex items-center justify-between gap-3 rounded-lg border p-3"
            >
              <div className="min-w-0">
                <Link
                  href={`/mods/${encodeURIComponent(claim.listingId)}`}
                  className="text-sm font-medium underline-offset-4 hover:underline"
                >
                  {claim.listingId}
                </Link>
                <div className="text-muted-foreground text-sm">
                  {claim.note ??
                    (claim.decidedAt
                      ? `Checked ${new Date(claim.decidedAt).toISOString().slice(0, 10)}`
                      : "Not checked yet")}
                </div>
              </div>
              <Badge
                variant={claim.status === "approved" ? "secondary" : "outline"}
              >
                {STATUS_LABEL[claim.status] ?? claim.status}
              </Badge>
            </div>
          ))
        )}
      </CardContent>
    </Card>
  );
}
