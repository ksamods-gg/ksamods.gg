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

type Submission = {
  id: string;
  listingId: string;
  state: string;
  prNumber: number | null;
  prUrl: string | null;
  error: string | null;
};

/** Separate from the claims card on purpose: a claim is "a mod I maintain",
 *  a submission is "a pull request I opened". Different things. */
const STATE_LABEL: Record<string, string> = {
  submitting: "Opening",
  open: "Awaiting review",
  merged: "Merged",
  closed: "Closed",
  failed: "Failed",
};

export function SubmissionsCard() {
  const [submissions, setSubmissions] = useState<Submission[] | null>(null);

  useEffect(() => {
    orpc.listings.submissions
      .mine()
      .then((rows) => setSubmissions(rows as Submission[]))
      .catch(() => setSubmissions([]));
  }, []);

  // An empty card for a feature most people will never use is noise.
  if (submissions !== null && submissions.length === 0) return null;

  return (
    <Card>
      <CardHeader>
        <CardTitle>Your submissions</CardTitle>
        <CardDescription>
          Listings you have proposed to the content index.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        {submissions === null ? (
          <p className="text-muted-foreground text-sm">Loading...</p>
        ) : (
          submissions.map((submission) => (
            <div
              key={submission.id}
              className="border-border flex items-center justify-between gap-3 rounded-lg border p-3"
            >
              <div className="min-w-0">
                <div className="text-sm font-medium">
                  {submission.listingId}
                </div>
                <div className="text-muted-foreground text-sm">
                  {submission.error ??
                    (submission.prUrl ? (
                      <a
                        className="underline underline-offset-4"
                        href={submission.prUrl}
                        target="_blank"
                        rel="noreferrer noopener"
                      >
                        Pull request{" "}
                        {submission.prNumber ? `#${submission.prNumber}` : ""}
                      </a>
                    ) : (
                      "Not opened yet"
                    ))}
                </div>
              </div>
              <Badge
                variant={
                  submission.state === "merged" ? "secondary" : "outline"
                }
              >
                {STATE_LABEL[submission.state] ?? submission.state}
              </Badge>
            </div>
          ))
        )}

        <Button
          render={<Link href="/mods/new" />}
          nativeButton={false}
          variant="outline"
          size="sm"
        >
          Submit another mod
        </Button>
      </CardContent>
    </Card>
  );
}
