"use client";

import { useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Label } from "@/components/ui/label";

export type Preview = {
  toml: string | null;
  path: string;
  baseRepo: string;
  baseBranch: string;
  blockers: { field?: string; message: string; code?: string }[];
  ownership: {
    proof: "owner-id" | "topic" | "marker" | null;
    selfMerges: boolean;
    authority: string | null;
    remediation: string[];
  };
};

export type Submitted = { prNumber: number | null; prUrl: string | null };

const PROOF_TEXT: Record<string, string> = {
  "owner-id":
    "Your GitHub account owns this repository, so this merges automatically.",
  topic:
    "The repository topic proves you control it, so this merges automatically.",
  marker:
    "The marker file proves you control it, so this merges automatically.",
};

export function PreviewDialog({
  preview,
  submitted,
  busy,
  error,
  onSubmit,
  onClose,
}: {
  preview: Preview | null;
  submitted: Submitted | null;
  busy: boolean;
  error: string | null;
  onSubmit: (acknowledge: boolean) => void;
  onClose: () => void;
}) {
  const [acknowledged, setAcknowledged] = useState(false);
  const [copied, setCopied] = useState(false);

  if (!preview) return null;

  const { ownership } = preview;
  // A blocker the acknowledgement can clear, as opposed to a real problem.
  const unproven = preview.blockers.some(
    (blocker) => blocker.code === "ownership_unproven",
  );
  const hard = preview.blockers.filter(
    (blocker) => blocker.code !== "ownership_unproven",
  );
  const canSubmit =
    hard.length === 0 && (ownership.selfMerges || acknowledged || !unproven);

  async function copy() {
    if (!preview?.toml) return;
    await navigator.clipboard.writeText(preview.toml);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  }

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="max-h-[85vh] sm:max-w-2xl overflow-y-auto">
        <DialogHeader>
          <DialogTitle>
            {submitted ? "Pull request opened" : "Review your submission"}
          </DialogTitle>
          <DialogDescription>
            {submitted
              ? "Upstream checks run next. They can take a few minutes."
              : `This commits ${preview.path} to ${preview.baseRepo} on the ${preview.baseBranch} branch, as a pull request from your GitHub account.`}
          </DialogDescription>
        </DialogHeader>

        {submitted ? (
          <div className="min-w-0 space-y-4">
            <p role="status" className="text-sm">
              Your listing has been proposed. Track it on GitHub, and it will
              show up under your account here.
            </p>
            {submitted.prUrl && (
              <Button
                render={
                  <a
                    href={submitted.prUrl}
                    target="_blank"
                    rel="noreferrer noopener"
                  />
                }
                nativeButton={false}
              >
                View pull request{" "}
                {submitted.prNumber ? `#${submitted.prNumber}` : ""}
              </Button>
            )}
          </div>
        ) : (
          <div className="min-w-0 space-y-5">
            {hard.length > 0 && (
              <div role="alert" className="space-y-2">
                <p className="text-destructive text-sm font-medium">
                  {hard.length === 1
                    ? "One thing needs fixing"
                    : `${hard.length} things need fixing`}
                </p>
                <ul className="text-destructive space-y-1 text-sm">
                  {hard.map((blocker, index) => (
                    <li key={index}>
                      {blocker.field ? `${blocker.field}: ` : ""}
                      {blocker.message}
                    </li>
                  ))}
                </ul>
              </div>
            )}

            <div className="space-y-2">
              <div className="flex items-center justify-between gap-2">
                <h3 className="text-sm font-medium">Verification</h3>
                <Badge variant={ownership.selfMerges ? "secondary" : "outline"}>
                  {ownership.selfMerges
                    ? "Merges automatically"
                    : "Needs a steward"}
                </Badge>
              </div>
              <p className="text-muted-foreground text-sm text-pretty">
                {ownership.proof
                  ? PROOF_TEXT[ownership.proof]
                  : ownership.authority
                    ? `We could not confirm you control ${ownership.authority}.`
                    : "This listing names no GitHub repository, so ownership cannot be checked automatically."}
              </p>
              {ownership.remediation.length > 0 && (
                <ul className="text-muted-foreground list-disc space-y-1 pl-5 text-sm">
                  {ownership.remediation.map((step) => (
                    <li key={step}>{step}</li>
                  ))}
                </ul>
              )}
            </div>

            {preview.toml && (
              <div className="min-w-0 space-y-2">
                <div className="flex items-center justify-between gap-2">
                  <h3 className="truncate font-mono text-sm font-medium">
                    {preview.path}
                  </h3>
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    onClick={copy}
                  >
                    {copied ? "Copied" : "Copy"}
                  </Button>
                </div>
                {/* The exact bytes that get committed, so nobody proposes a file
                    under their own name without having seen it. */}
                <pre className="bg-muted max-h-72 overflow-y-auto rounded-lg p-3 font-mono text-xs break-words whitespace-pre-wrap">
                  {preview.toml}
                </pre>
              </div>
            )}

            {error && (
              <p role="alert" className="text-destructive text-sm">
                {error}
              </p>
            )}
          </div>
        )}

        <DialogFooter>
          {submitted ? (
            <Button variant="outline" onClick={onClose}>
              Done
            </Button>
          ) : (
            <div className="flex w-full flex-col gap-3">
              {unproven && hard.length === 0 && (
                <div className="flex items-start gap-2">
                  <Checkbox
                    id="acknowledge"
                    checked={acknowledged}
                    onCheckedChange={(checked) =>
                      setAcknowledged(checked === true)
                    }
                  />
                  <Label
                    htmlFor="acknowledge"
                    className="text-muted-foreground font-normal"
                  >
                    I understand a steward has to review this before it merges
                  </Label>
                </div>
              )}
              <div className="flex justify-end gap-2">
                <Button variant="outline" onClick={onClose} disabled={busy}>
                  Back
                </Button>
                <Button
                  onClick={() => onSubmit(acknowledged)}
                  disabled={!canSubmit || busy}
                >
                  {busy ? "Opening..." : "Open pull request"}
                </Button>
              </div>
            </div>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
