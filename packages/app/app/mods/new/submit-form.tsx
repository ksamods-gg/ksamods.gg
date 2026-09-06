"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { StringList } from "@/components/string-list";
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
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import { authClient, useSession } from "@/lib/auth-client";
import { orpc } from "@/lib/orpc-browser";
import {
  type Draft,
  type Errors,
  LICENSES,
  emptyDraft,
  toDocument,
  validate,
} from "@/lib/submission";
import { type Preview, PreviewDialog, type Submitted } from "./preview-dialog";

const DRAFT_VERSION = 1;
const draftKey = (userId: string) => `ksamods:draft:${userId}`;

const ANCHORS = [
  { value: "mods", label: "mods" },
  { value: "user-data", label: "user-data" },
  { value: "game-root", label: "game-root" },
  { value: "standalone", label: "standalone" },
];

const DEP_KINDS = [
  "required",
  "optional",
  "recommends",
  "suggests",
  "conflict",
].map((kind) => ({
  value: kind,
  label: kind,
}));

/** Readable names for the error summary. A dotted path is a fine key and a
 *  poor label, and several of these live in sections that start collapsed. */
const FIELD_LABELS: Record<string, string> = {
  id: "Id",
  name: "Name",
  authors: "Authors",
  abstract: "Abstract",
  licenseOther: "License",
  "links.forums": "Forums thread",
  "links.repository": "Repository link",
  "links.bugtracker": "Bug tracker link",
  "links.discussions": "Discussions link",
  "links.homepage": "Homepage link",
  "links.spacedock": "SpaceDock link",
  "compatibility.game_min": "Oldest game build",
  "releases.github": "GitHub repository",
  "releases.spacedock": "SpaceDock mod id",
  "releases.authority": "Release authority",
  "loader.id": "Loader id",
  "loader.min": "Loader oldest version",
  "loader.max": "Loader newest version",
  "install.target": "Install anchor",
  "provides.launch": "Executable to launch",
  "provides.contentDir": "Content directory",
  superseded_by: "Replaced by",
};

function labelFor(key: string) {
  if (FIELD_LABELS[key]) return FIELD_LABELS[key];
  const tag = key.match(/^tags\.(\d+)$/);
  if (tag) return `Tag ${Number(tag[1]) + 1}`;
  const dependency = key.match(/^dependencies\.(\d+)\.(\w+)$/);
  if (dependency)
    return `Dependency ${Number(dependency[1]) + 1} ${dependency[2]}`;
  return key;
}

/** A collapsible section. Native details rather than an accordion component:
 *  keyboard and screen reader accessible for free, and no open state to keep. */
function Section({
  title,
  hint,
  children,
}: {
  title: string;
  hint: string;
  children: React.ReactNode;
}) {
  return (
    <details className="border-border group rounded-lg border">
      <summary className="cursor-pointer list-none px-4 py-3">
        <span className="font-medium">{title}</span>
        <span className="text-muted-foreground ml-2 text-sm">{hint}</span>
      </summary>
      <div className="space-y-4 border-t px-4 py-4">{children}</div>
    </details>
  );
}

export function SubmitForm({
  takenIds,
  gameVersions,
}: {
  takenIds: string[];
  gameVersions: string[];
}) {
  const router = useRouter();
  const { data: session, isPending } = useSession();

  const [draft, setDraft] = useState<Draft>(emptyDraft);
  const [restoredAt, setRestoredAt] = useState<string | null>(null);
  const [loaded, setLoaded] = useState(false);
  const [touched, setTouched] = useState<Set<string>>(new Set());
  const [errors, setErrors] = useState<Errors>({});
  const [hasScope, setHasScope] = useState<boolean | null>(null);

  const [preview, setPreview] = useState<Preview | null>(null);
  const [previewedFor, setPreviewedFor] = useState("");
  const [submitted, setSubmitted] = useState<Submitted | null>(null);
  const [busy, setBusy] = useState(false);
  const [dialogError, setDialogError] = useState<string | null>(null);
  /** Shown after a failed preview. Without it a validation failure in a
   *  collapsed section is completely invisible and the button looks dead. */
  const [summary, setSummary] = useState(false);

  useEffect(() => {
    if (!isPending && !session) router.replace("/sign-in?next=/mods/new");
  }, [isPending, session, router]);

  // Restore before the user can navigate anywhere, so the GitHub round trip,
  // a refresh and the back button are all one mechanism.
  useEffect(() => {
    if (!session || loaded) return;
    try {
      const stored = localStorage.getItem(draftKey(session.user.id));
      if (stored) {
        const parsed = JSON.parse(stored);
        if (parsed?.v === DRAFT_VERSION && parsed.draft) {
          setDraft({ ...emptyDraft(), ...parsed.draft });
          setRestoredAt(parsed.savedAt ?? null);
        } else {
          localStorage.removeItem(draftKey(session.user.id));
        }
      }
    } catch {
      // A private window, or a browser that refuses storage. The form still
      // works; only the round trip loses its safety net.
    }
    setLoaded(true);
  }, [session, loaded]);

  useEffect(() => {
    if (!session || !loaded) return;
    try {
      localStorage.setItem(
        draftKey(session.user.id),
        JSON.stringify({
          v: DRAFT_VERSION,
          savedAt: new Date().toISOString(),
          draft,
        }),
      );
    } catch {
      // Storage is full or refused. Nothing to do, and a banner about it would
      // be noise in the common case.
    }
  }, [draft, session, loaded]);

  // Advisory only: the server reads the live scope header and is the real gate.
  useEffect(() => {
    if (!session) return;
    authClient
      .listAccounts()
      .then(({ data }) => {
        const github = (
          data as { providerId: string; scopes?: string[] }[] | undefined
        )?.find((account) => account.providerId === "github");
        setHasScope(
          Boolean(
            github?.scopes?.some((s) => s === "public_repo" || s === "repo"),
          ),
        );
      })
      .catch(() => setHasScope(false));
  }, [session]);

  if (isPending || !session) return null;

  const patch = (values: Partial<Draft>) =>
    setDraft((current) => ({ ...current, ...values }));
  const isMod = draft.type === "mod";

  const show = (key: string) => (touched.has(key) ? errors[key] : undefined);
  const blur = (key: string) => () => {
    setTouched((current) => new Set(current).add(key));
    setErrors(validate(draft, takenIds));
  };

  /** Re-validates as you fix a field that is already showing an error. */
  const revalidate = (next: Draft) => {
    setDraft(next);
    if (Object.keys(errors).length > 0) setErrors(validate(next, takenIds));
  };

  function field(key: string, error?: string) {
    return {
      id: key,
      "aria-invalid": Boolean(error),
      "aria-describedby": error ? `${key}-error` : undefined,
      onBlur: blur(key),
    };
  }

  function connectGithub() {
    void authClient.linkSocial({
      provider: "github",
      // Opening the pull request as the user is the whole point, so this needs
      // write access to public repositories.
      scopes: ["public_repo"],
      callbackURL: `${window.location.origin}/mods/new`,
    });
  }

  /**
   * Jumps to a field. Optional sections are collapsed `details` elements, so a
   * bare focus() would land on a hidden input and look like nothing happened,
   * which is exactly how a failed preview used to present itself.
   */
  function jumpTo(key: string) {
    const target = document.getElementById(key);
    if (!target) return;
    target.closest("details")?.setAttribute("open", "");
    target.scrollIntoView({ block: "center", behavior: "smooth" });
    target.focus({ preventScroll: true });
  }

  async function openPreview() {
    const found = validate(draft, takenIds);
    setErrors(found);
    if (Object.keys(found).length > 0) {
      // Do not ask the server what we already know.
      setTouched(new Set(Object.keys(found)));
      setSummary(true);
      jumpTo(Object.keys(found)[0]!);
      return;
    }
    setSummary(false);

    setBusy(true);
    setDialogError(null);
    try {
      const document_ = toDocument(draft);
      const result = await orpc.listings.submissions.preview({
        document: document_,
      });
      setPreview(result as Preview);
      setPreviewedFor(JSON.stringify(document_));

      // Server blockers carry the same dotted keys, so they land under the
      // right input rather than in a heap at the top.
      const mapped: Errors = {};
      for (const blocker of result.blockers) {
        if (blocker.field) mapped[blocker.field] = blocker.message;
      }
      if (Object.keys(mapped).length > 0) {
        setErrors((current) => ({ ...current, ...mapped }));
        setTouched((current) => new Set([...current, ...Object.keys(mapped)]));
      }
    } catch (caught) {
      setDialogError(
        caught instanceof Error ? caught.message : "The check could not run",
      );
      setPreview(null);
    } finally {
      setBusy(false);
    }
  }

  async function submit(acknowledge: boolean) {
    setBusy(true);
    setDialogError(null);
    try {
      const document_ = toDocument(draft);
      // Never submit against a preview the user is no longer looking at.
      if (JSON.stringify(document_) !== previewedFor) {
        setDialogError("The form changed. Close this and preview again.");
        return;
      }

      const result = await orpc.listings.submissions.submit({
        document: document_,
        acknowledgeStewardReview: acknowledge,
      });
      setSubmitted({ prNumber: result.prNumber, prUrl: result.prUrl });
      try {
        localStorage.removeItem(draftKey(session!.user.id));
      } catch {
        // Nothing to clean up if storage was never available.
      }
    } catch (caught) {
      setDialogError(
        caught instanceof Error
          ? caught.message
          : "The submission could not be opened",
      );
    } finally {
      setBusy(false);
    }
  }

  const problems = summary ? Object.keys(errors) : [];

  return (
    <div className="space-y-6">
      {problems.length > 0 && (
        <div
          role="alert"
          className="border-destructive/40 space-y-2 rounded-lg border p-4"
        >
          <p className="text-destructive text-sm font-medium">
            {problems.length === 1
              ? "One thing needs fixing before you can preview"
              : `${problems.length} things need fixing before you can preview`}
          </p>
          <ul className="space-y-1 text-sm">
            {problems.map((key) => (
              <li key={key}>
                <button
                  type="button"
                  className="text-destructive underline underline-offset-4"
                  onClick={() => jumpTo(key)}
                >
                  {labelFor(key)}
                </button>
                <span className="text-muted-foreground">: {errors[key]}</span>
              </li>
            ))}
          </ul>
        </div>
      )}

      {restoredAt && (
        <div
          role="status"
          className="border-border flex items-center justify-between gap-3 rounded-lg border p-3"
        >
          <p className="text-muted-foreground text-sm">
            Restored a draft you saved on{" "}
            {new Date(restoredAt).toISOString().slice(0, 10)}.
          </p>
          <Button
            variant="ghost"
            size="sm"
            onClick={() => {
              setDraft(emptyDraft());
              setRestoredAt(null);
              setErrors({});
              setTouched(new Set());
              try {
                localStorage.removeItem(draftKey(session.user.id));
              } catch {}
            }}
          >
            Discard
          </Button>
        </div>
      )}

      {hasScope === false && (
        <Card>
          <CardHeader>
            <CardTitle>Connect GitHub</CardTitle>
            <CardDescription>
              The pull request is opened as you, because the index verifies
              ownership against the account that opens it. That needs write
              access to your public repositories.
            </CardDescription>
          </CardHeader>
          <CardContent>
            <Button onClick={connectGithub}>Connect GitHub</Button>
          </CardContent>
        </Card>
      )}

      <Card>
        <CardHeader>
          <CardTitle>Basics</CardTitle>
          <CardDescription>Everything here is required.</CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="id">Id</Label>
            <Input
              {...field("id", show("id"))}
              value={draft.id}
              placeholder="AdvancedFlightComputer"
              onChange={(event) =>
                revalidate({ ...draft, id: event.target.value })
              }
            />
            <p className="text-muted-foreground text-sm">
              The folder your mod installs as. This can never be renamed, so
              choose carefully.
            </p>
            {show("id") && (
              <p
                id="id-error"
                role="alert"
                className="text-destructive text-sm"
              >
                {show("id")}
              </p>
            )}
          </div>

          <div className="space-y-2">
            <Label htmlFor="type">Type</Label>
            <Select
              items={[
                { value: "mod", label: "Mod" },
                { value: "mod-loader", label: "Mod loader" },
              ]}
              value={draft.type}
              onValueChange={(value) =>
                patch({ type: String(value) as Draft["type"] })
              }
            >
              <SelectTrigger id="type" className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="mod">Mod</SelectItem>
                <SelectItem value="mod-loader">Mod loader</SelectItem>
              </SelectContent>
            </Select>
          </div>

          <div className="space-y-2">
            <Label htmlFor="name">Name</Label>
            <Input
              {...field("name", show("name"))}
              value={draft.name}
              placeholder="Advanced Flight Computer"
              onChange={(event) =>
                revalidate({ ...draft, name: event.target.value })
              }
            />
            {show("name") && (
              <p
                id="name-error"
                role="alert"
                className="text-destructive text-sm"
              >
                {show("name")}
              </p>
            )}
          </div>

          <StringList
            id="authors"
            label="Authors"
            values={draft.authors}
            onChange={(authors) => revalidate({ ...draft, authors })}
            placeholder="Display name"
            addLabel="Add author"
            errors={show("authors") ? { 0: show("authors") } : undefined}
          />

          <div className="space-y-2">
            <Label htmlFor="abstract">Abstract</Label>
            <Textarea
              {...field("abstract", show("abstract"))}
              value={draft.abstract}
              rows={2}
              placeholder="One or two sentences for list and search views."
              onChange={(event) =>
                revalidate({ ...draft, abstract: event.target.value })
              }
            />
            {show("abstract") && (
              <p
                id="abstract-error"
                role="alert"
                className="text-destructive text-sm"
              >
                {show("abstract")}
              </p>
            )}
          </div>

          <div className="space-y-2">
            <Label htmlFor="license">License</Label>
            <Select
              items={[
                ...LICENSES.map((value) => ({ value, label: value })),
                { value: "other", label: "Other (SPDX expression)" },
              ]}
              value={draft.license}
              onValueChange={(value) => patch({ license: String(value) })}
            >
              <SelectTrigger id="license" className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent className="max-h-72">
                {LICENSES.map((value) => (
                  <SelectItem key={value} value={value}>
                    {value}
                  </SelectItem>
                ))}
                <SelectItem value="other">Other (SPDX expression)</SelectItem>
              </SelectContent>
            </Select>
            {draft.license === "other" && (
              <>
                <Input
                  {...field("licenseOther", show("licenseOther"))}
                  value={draft.licenseOther}
                  placeholder="MIT OR Apache-2.0"
                  onChange={(event) =>
                    revalidate({ ...draft, licenseOther: event.target.value })
                  }
                />
                <p className="text-muted-foreground text-sm">
                  Must be a real SPDX identifier or expression. The index checks
                  it against the official list, so a typo is rejected there
                  rather than here.
                </p>
              </>
            )}
            {show("licenseOther") && (
              <p
                id="licenseOther-error"
                role="alert"
                className="text-destructive text-sm"
              >
                {show("licenseOther")}
              </p>
            )}
          </div>

          <div className="space-y-2">
            <Label htmlFor="links.forums">Forums thread</Label>
            <Input
              {...field("links.forums", show("links.forums"))}
              value={draft.links.forums}
              placeholder="https://forums.ahwoo.com/threads/your-mod.123/"
              onChange={(event) =>
                revalidate({
                  ...draft,
                  links: { ...draft.links, forums: event.target.value },
                })
              }
            />
            <p className="text-muted-foreground text-sm">
              Required. It ties the listing to your Ahwoo account and settles
              who claimed an id first.
            </p>
            {show("links.forums") && (
              <p
                id="links.forums-error"
                role="alert"
                className="text-destructive text-sm"
              >
                {show("links.forums")}
              </p>
            )}
          </div>

          <div className="space-y-2">
            <Label htmlFor="compatibility.game_min">
              Oldest game build known to work
            </Label>
            <Select
              items={gameVersions.map((value) => ({ value, label: value }))}
              value={draft.compatibility.game_min}
              onValueChange={(value) =>
                revalidate({
                  ...draft,
                  compatibility: {
                    ...draft.compatibility,
                    game_min: String(value),
                  },
                })
              }
            >
              <SelectTrigger id="compatibility.game_min" className="w-full">
                <SelectValue placeholder="Pick a build" />
              </SelectTrigger>
              <SelectContent className="max-h-72">
                {gameVersions.map((value) => (
                  <SelectItem key={value} value={value}>
                    {value}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            {show("compatibility.game_min") && (
              <p
                id="compatibility.game_min-error"
                role="alert"
                className="text-destructive text-sm"
              >
                {show("compatibility.game_min")}
              </p>
            )}
          </div>
        </CardContent>
      </Card>

      <div className="space-y-3">
        <h2 className="text-sm font-semibold">Optional detail</h2>

        <Section title="Description" hint="Longer text, Markdown">
          <Textarea
            id="description"
            value={draft.description}
            rows={8}
            placeholder="What it does, how to use it, anything a player should know."
            onChange={(event) => patch({ description: event.target.value })}
          />
        </Section>

        <Section title="Where releases come from" hint="GitHub or SpaceDock">
          <div className="space-y-2">
            <Label htmlFor="releases.github">GitHub repository</Label>
            <Input
              {...field("releases.github", show("releases.github"))}
              value={draft.releases.github}
              placeholder="owner/repo"
              onChange={(event) =>
                revalidate({
                  ...draft,
                  releases: { ...draft.releases, github: event.target.value },
                })
              }
            />
            <p className="text-muted-foreground text-sm">
              Releases published here are picked up automatically. This is also
              what proves you control the mod, so a listing without it needs a
              steward to review it.
            </p>
            {show("releases.github") && (
              <p
                id="releases.github-error"
                role="alert"
                className="text-destructive text-sm"
              >
                {show("releases.github")}
              </p>
            )}
          </div>

          <div className="space-y-2">
            <Label htmlFor="releases.spacedock">SpaceDock mod id</Label>
            <Input
              {...field("releases.spacedock", show("releases.spacedock"))}
              value={draft.releases.spacedock}
              placeholder="4253"
              inputMode="numeric"
              onChange={(event) =>
                revalidate({
                  ...draft,
                  releases: {
                    ...draft.releases,
                    spacedock: event.target.value,
                  },
                })
              }
            />
            {show("releases.spacedock") && (
              <p
                id="releases.spacedock-error"
                role="alert"
                className="text-destructive text-sm"
              >
                {show("releases.spacedock")}
              </p>
            )}
          </div>

          {draft.releases.github && draft.releases.spacedock && (
            <div className="space-y-2">
              <Label htmlFor="releases.authority">
                Which host defines the releases
              </Label>
              <Select
                items={[
                  { value: "github", label: "GitHub" },
                  { value: "spacedock", label: "SpaceDock" },
                ]}
                value={draft.releases.authority}
                onValueChange={(value) =>
                  revalidate({
                    ...draft,
                    releases: {
                      ...draft.releases,
                      authority: String(
                        value,
                      ) as Draft["releases"]["authority"],
                    },
                  })
                }
              >
                <SelectTrigger id="releases.authority" className="w-full">
                  <SelectValue placeholder="Pick one" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="github">GitHub</SelectItem>
                  <SelectItem value="spacedock">SpaceDock</SelectItem>
                </SelectContent>
              </Select>
              {show("releases.authority") && (
                <p
                  id="releases.authority-error"
                  role="alert"
                  className="text-destructive text-sm"
                >
                  {show("releases.authority")}
                </p>
              )}
            </div>
          )}
        </Section>

        <Section title="Other links" hint="Repository, issues, homepage">
          {(
            [
              "repository",
              "bugtracker",
              "discussions",
              "homepage",
              "spacedock",
            ] as const
          ).map((key) => (
            <div key={key} className="space-y-2">
              <Label htmlFor={`links.${key}`}>{key}</Label>
              <Input
                {...field(`links.${key}`, show(`links.${key}`))}
                value={draft.links[key]}
                placeholder="https://"
                onChange={(event) =>
                  revalidate({
                    ...draft,
                    links: { ...draft.links, [key]: event.target.value },
                  })
                }
              />
              {show(`links.${key}`) && (
                <p
                  id={`links.${key}-error`}
                  role="alert"
                  className="text-destructive text-sm"
                >
                  {show(`links.${key}`)}
                </p>
              )}
            </div>
          ))}
        </Section>

        <Section
          title="Compatibility and tags"
          hint="Newest build, platforms, categories"
        >
          <div className="space-y-2">
            <Label htmlFor="compatibility.game_max">
              Newest tested game build
            </Label>
            <Select
              items={[
                { value: "", label: "No known upper limit" },
                ...gameVersions.map((value) => ({ value, label: value })),
              ]}
              value={draft.compatibility.game_max}
              onValueChange={(value) =>
                patch({
                  compatibility: {
                    ...draft.compatibility,
                    game_max: String(value),
                  },
                })
              }
            >
              <SelectTrigger id="compatibility.game_max" className="w-full">
                <SelectValue placeholder="No known upper limit" />
              </SelectTrigger>
              <SelectContent className="max-h-72">
                <SelectItem value="">No known upper limit</SelectItem>
                {gameVersions.map((value) => (
                  <SelectItem key={value} value={value}>
                    {value}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <p className="text-muted-foreground text-sm">
              Leaving this open is the recommended default.
            </p>
          </div>

          <StringList
            id="tags"
            label="Tags"
            values={draft.tags.length ? draft.tags : [""]}
            onChange={(tags) => revalidate({ ...draft, tags })}
            placeholder="information"
            hint="Lowercase, dash separated."
            addLabel="Add tag"
            errors={Object.fromEntries(
              draft.tags
                .map((_, index) => [index, show(`tags.${index}`)])
                .filter(([, e]) => e),
            )}
          />
        </Section>

        {isMod && (
          <Section title="Loader" hint="If your mod needs one">
            <div className="grid gap-4 sm:grid-cols-3">
              {(["id", "min", "max"] as const).map((key) => (
                <div key={key} className="space-y-2">
                  <Label htmlFor={`loader.${key}`}>
                    {key === "id"
                      ? "Loader id"
                      : key === "min"
                        ? "Oldest version"
                        : "Newest version"}
                  </Label>
                  <Input
                    {...field(`loader.${key}`, show(`loader.${key}`))}
                    value={draft.loader[key]}
                    placeholder={key === "id" ? "StarMap" : "0.4.5"}
                    onChange={(event) =>
                      revalidate({
                        ...draft,
                        loader: { ...draft.loader, [key]: event.target.value },
                      })
                    }
                  />
                  {show(`loader.${key}`) && (
                    <p
                      id={`loader.${key}-error`}
                      role="alert"
                      className="text-destructive text-sm"
                    >
                      {show(`loader.${key}`)}
                    </p>
                  )}
                </div>
              ))}
            </div>
          </Section>
        )}

        <Section
          title="Dependencies"
          hint="Other mods yours needs or conflicts with"
        >
          <div className="space-y-3">
            {draft.dependencies.map((entry, index) => (
              <div
                key={index}
                className="border-border space-y-3 rounded-lg border p-3"
              >
                <div className="grid gap-3 sm:grid-cols-2">
                  <div className="space-y-2">
                    <Label htmlFor={`dependencies.${index}.id`}>Mod id</Label>
                    <Input
                      {...field(
                        `dependencies.${index}.id`,
                        show(`dependencies.${index}.id`),
                      )}
                      value={entry.id}
                      placeholder="SomeMod"
                      onChange={(event) =>
                        revalidate({
                          ...draft,
                          dependencies: draft.dependencies.map((d, at) =>
                            at === index ? { ...d, id: event.target.value } : d,
                          ),
                        })
                      }
                    />
                    {show(`dependencies.${index}.id`) && (
                      <p
                        id={`dependencies.${index}.id-error`}
                        role="alert"
                        className="text-destructive text-sm"
                      >
                        {show(`dependencies.${index}.id`)}
                      </p>
                    )}
                  </div>
                  <div className="space-y-2">
                    <Label htmlFor={`dependencies.${index}.kind`}>Kind</Label>
                    <Select
                      items={DEP_KINDS}
                      value={entry.kind}
                      onValueChange={(value) =>
                        patch({
                          dependencies: draft.dependencies.map((d, at) =>
                            at === index
                              ? { ...d, kind: String(value) as typeof d.kind }
                              : d,
                          ),
                        })
                      }
                    >
                      <SelectTrigger
                        id={`dependencies.${index}.kind`}
                        className="w-full"
                      >
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        {DEP_KINDS.map((kind) => (
                          <SelectItem key={kind.value} value={kind.value}>
                            {kind.label}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  </div>
                </div>

                <div className="flex items-end gap-3">
                  {(["min", "max"] as const).map((key) => (
                    <div key={key} className="flex-1 space-y-2">
                      <Label htmlFor={`dependencies.${index}.${key}`}>
                        {key === "min" ? "Oldest" : "Newest"}
                      </Label>
                      <Input
                        {...field(
                          `dependencies.${index}.${key}`,
                          show(`dependencies.${index}.${key}`),
                        )}
                        value={entry[key]}
                        placeholder="1.2.3"
                        onChange={(event) =>
                          revalidate({
                            ...draft,
                            dependencies: draft.dependencies.map((d, at) =>
                              at === index
                                ? { ...d, [key]: event.target.value }
                                : d,
                            ),
                          })
                        }
                      />
                    </div>
                  ))}
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    onClick={() =>
                      revalidate({
                        ...draft,
                        dependencies: draft.dependencies.filter(
                          (_, at) => at !== index,
                        ),
                      })
                    }
                  >
                    Remove
                  </Button>
                </div>
              </div>
            ))}
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() =>
                patch({
                  dependencies: [
                    ...draft.dependencies,
                    { id: "", kind: "required", min: "", max: "" },
                  ],
                })
              }
            >
              Add dependency
            </Button>
          </div>
        </Section>

        <Section
          title="Install"
          hint="Where it goes and what a person must do by hand"
        >
          {isMod ? (
            <p className="text-muted-foreground text-sm">
              A mod installs to the <code>mods</code> anchor. The game decides
              where it goes, so there is nothing to set here.
            </p>
          ) : (
            <div className="space-y-2">
              <Label htmlFor="install.target">Anchor</Label>
              <Select
                items={ANCHORS}
                value={draft.install.target}
                onValueChange={(value) =>
                  revalidate({
                    ...draft,
                    install: {
                      ...draft.install,
                      target: String(value) as Draft["install"]["target"],
                    },
                  })
                }
              >
                <SelectTrigger id="install.target" className="w-full">
                  <SelectValue placeholder="Pick an anchor" />
                </SelectTrigger>
                <SelectContent>
                  {ANCHORS.map((anchor) => (
                    <SelectItem key={anchor.value} value={anchor.value}>
                      {anchor.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              {show("install.target") && (
                <p
                  id="install.target-error"
                  role="alert"
                  className="text-destructive text-sm"
                >
                  {show("install.target")}
                </p>
              )}
            </div>
          )}

          <div className="space-y-2">
            <Label htmlFor="install.root">Directory inside the archive</Label>
            <Input
              {...field("install.root")}
              value={draft.install.root}
              placeholder="Leave empty to derive it"
              onChange={(event) =>
                patch({
                  install: { ...draft.install, root: event.target.value },
                })
              }
            />
          </div>

          {(["manages", "steps", "uninstall"] as const).map((key) => (
            <div key={key} className="space-y-2">
              <Label htmlFor={`install.${key}`}>
                {key === "manages"
                  ? "Paths this mod owns"
                  : key === "steps"
                    ? "Manual install steps"
                    : "Uninstall steps"}
              </Label>
              <Textarea
                id={`install.${key}`}
                rows={3}
                value={draft.install[key]}
                placeholder="One per line"
                onChange={(event) =>
                  patch({
                    install: { ...draft.install, [key]: event.target.value },
                  })
                }
              />
              <p className="text-muted-foreground text-sm">
                {key === "manages"
                  ? "One path per line. A manager will not edit these."
                  : "One per line, written for a person to read."}
              </p>
            </div>
          ))}
        </Section>

        {!isMod && (
          <Section
            title="Provides"
            hint="What your loader offers the mods under it"
          >
            <div className="space-y-2">
              <Label htmlFor="provides.launch">Executable to launch</Label>
              <Input
                {...field("provides.launch", show("provides.launch"))}
                value={draft.provides.launch}
                placeholder="StarMap.exe"
                onChange={(event) =>
                  revalidate({
                    ...draft,
                    provides: { ...draft.provides, launch: event.target.value },
                  })
                }
              />
              {show("provides.launch") && (
                <p
                  id="provides.launch-error"
                  role="alert"
                  className="text-destructive text-sm"
                >
                  {show("provides.launch")}
                </p>
              )}
            </div>

            <div className="space-y-2">
              <Label htmlFor="provides.contentDir">Content directory</Label>
              <Select
                items={ANCHORS}
                value={draft.provides.contentDir}
                onValueChange={(value) =>
                  revalidate({
                    ...draft,
                    provides: {
                      ...draft.provides,
                      contentDir: String(
                        value,
                      ) as Draft["provides"]["contentDir"],
                    },
                  })
                }
              >
                <SelectTrigger id="provides.contentDir" className="w-full">
                  <SelectValue placeholder="None" />
                </SelectTrigger>
                <SelectContent>
                  {ANCHORS.map((anchor) => (
                    <SelectItem key={anchor.value} value={anchor.value}>
                      {anchor.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              {show("provides.contentDir") && (
                <p
                  id="provides.contentDir-error"
                  role="alert"
                  className="text-destructive text-sm"
                >
                  {show("provides.contentDir")}
                </p>
              )}
            </div>

            <div className="space-y-2">
              <Label htmlFor="provides.contentPath">Content path</Label>
              <Input
                id="provides.contentPath"
                value={draft.provides.contentPath}
                // Disabled until it can be valid, which beats an error message.
                disabled={!draft.provides.contentDir}
                placeholder={
                  draft.provides.contentDir
                    ? "mods"
                    : "Pick a content directory first"
                }
                onChange={(event) =>
                  patch({
                    provides: {
                      ...draft.provides,
                      contentPath: event.target.value,
                    },
                  })
                }
              />
            </div>
          </Section>
        )}
      </div>

      <div className="flex flex-wrap items-center gap-3">
        <Button onClick={openPreview} disabled={busy}>
          {busy && !preview ? "Checking..." : "Preview submission"}
        </Button>
        <p className="text-muted-foreground text-sm">
          You will see the exact file before anything is opened.
        </p>
      </div>

      {dialogError && !preview && (
        <p role="alert" className="text-destructive text-sm">
          {dialogError}
        </p>
      )}

      <PreviewDialog
        preview={preview}
        submitted={submitted}
        busy={busy}
        error={dialogError}
        onSubmit={submit}
        onClose={() => {
          setPreview(null);
          setDialogError(null);
          if (submitted) router.push("/account");
        }}
      />
    </div>
  );
}
