import Link from "next/link";
import { notFound } from "next/navigation";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { githubSlug, latestRelease } from "@/lib/listing";
import { SITE_CONTAINER } from "@/lib/layout";
import { orpc } from "@/lib/orpc";

// The snapshot changes when the worker pulls, so render per request.
export const dynamic = "force-dynamic";

async function getListing(id: string) {
  const snapshot = await orpc.contentIndex.latest().catch(() => null);
  // Exact match against the snapshot. The route param is never interpolated
  // into anything.
  return snapshot?.data.listings.find((listing) => listing.id === id) ?? null;
}

export async function generateMetadata({
  params,
}: {
  params: Promise<{ id: string }>;
}) {
  const listing = await getListing((await params).id);
  if (!listing) return { title: "Not found | ksamods.gg" };
  return {
    title: `${listing.authored.name} | ksamods.gg`,
    description: listing.authored.abstract,
  };
}

const LINK_LABELS: Record<string, string> = {
  repository: "Repository",
  forums: "Forums",
  spacedock: "SpaceDock",
  bugtracker: "Bug tracker",
  discussions: "Discussions",
  homepage: "Homepage",
};

export default async function ListingPage({
  params,
}: {
  params: Promise<{ id: string }>;
}) {
  const { id } = await params;
  const listing = await getListing(id);
  if (!listing) notFound();

  const maintainers = await orpc.listings
    .maintainers({ listingId: id })
    .catch(() => []);
  const { authored } = listing;
  const release = latestRelease(listing);
  const slug = githubSlug(listing);

  return (
    <main className={`${SITE_CONTAINER} flex-1 py-10`}>
      <div className="mb-8 space-y-3">
        <div className="flex flex-wrap items-center gap-2">
          <Badge variant="secondary">{authored.type}</Badge>
          {authored.status === "deprecated" && (
            <Badge variant="outline">deprecated</Badge>
          )}
          {authored.tags?.map((tag) => (
            <Badge key={tag} variant="outline">
              {tag}
            </Badge>
          ))}
        </div>
        <h1 className="text-3xl font-semibold tracking-tight">
          {authored.name}
        </h1>
        <p className="text-muted-foreground">
          by {authored.authors.join(", ")}
        </p>
        <p className="max-w-2xl text-pretty">{authored.abstract}</p>
      </div>

      <div className="grid gap-10 md:grid-cols-[1fr_280px]">
        <div className="space-y-8">
          {authored.description && (
            <section className="space-y-3">
              <h2 className="text-lg font-semibold">About</h2>
              {/* Upstream text, rendered as plain text rather than markup. */}
              <p className="text-muted-foreground text-sm whitespace-pre-wrap">
                {authored.description}
              </p>
            </section>
          )}

          <section className="space-y-3">
            <h2 className="text-lg font-semibold">Releases</h2>
            <ul className="space-y-2">
              {[...listing.releases]
                .sort((a, b) => b.release_date.localeCompare(a.release_date))
                .map((item) => (
                  <li
                    key={item.version}
                    className="border-border flex flex-wrap items-center justify-between gap-3 rounded-lg border p-3"
                  >
                    <div>
                      <div className="text-sm font-medium">v{item.version}</div>
                      <div className="text-muted-foreground text-sm">
                        {item.release_date.slice(0, 10)}, game {item.game_min}
                        {item.game_max ? ` to ${item.game_max}` : " and later"}
                      </div>
                    </div>
                    <Button
                      render={
                        <a href={item.download.url} rel="noreferrer noopener" />
                      }
                      nativeButton={false}
                      variant="outline"
                      size="sm"
                    >
                      Download
                    </Button>
                  </li>
                ))}
            </ul>
          </section>
        </div>

        <aside className="space-y-6 text-sm">
          <div className="space-y-2">
            <h2 className="font-semibold">Details</h2>
            <dl className="text-muted-foreground space-y-1">
              {release && (
                <div className="flex justify-between gap-3">
                  <dt>Latest</dt>
                  <dd className="text-foreground font-medium">
                    v{release.version}
                  </dd>
                </div>
              )}
              <div className="flex justify-between gap-3">
                <dt>License</dt>
                <dd>{authored.license}</dd>
              </div>
              <div className="flex justify-between gap-3">
                <dt>Game</dt>
                <dd className="text-right">
                  {authored.compatibility.game_min}
                  {authored.compatibility.game_max
                    ? ` to ${authored.compatibility.game_max}`
                    : " and later"}
                </dd>
              </div>
              {authored.loader && (
                <div className="flex justify-between gap-3">
                  <dt>Loader</dt>
                  <dd>
                    {authored.loader.id} {authored.loader.min}
                  </dd>
                </div>
              )}
            </dl>
          </div>

          <div className="space-y-2">
            <h2 className="font-semibold">Links</h2>
            <ul className="space-y-1">
              {Object.entries(authored.links).map(([key, value]) =>
                typeof value === "string" ? (
                  <li key={key}>
                    <a
                      className="text-muted-foreground underline underline-offset-4"
                      href={value}
                      target="_blank"
                      rel="noreferrer noopener"
                    >
                      {LINK_LABELS[key] ?? key}
                    </a>
                  </li>
                ) : null,
              )}
            </ul>
          </div>

          <div className="space-y-2">
            <h2 className="font-semibold">Maintainers</h2>
            {maintainers.length > 0 ? (
              <ul className="space-y-2">
                {maintainers.map((maintainer) => (
                  <li
                    key={maintainer.id}
                    className="flex items-center justify-between gap-2"
                  >
                    <span>{maintainer.name}</span>
                    <Badge variant="secondary">Verified</Badge>
                  </li>
                ))}
              </ul>
            ) : (
              <p className="text-muted-foreground">
                Nobody has claimed this listing yet.
              </p>
            )}
            {/* Shown to signed-out visitors too, otherwise the author who
                arrives from a forum link never learns claiming exists. */}
            <Button
              render={
                <Link href={`/mods/${encodeURIComponent(listing.id)}/claim`} />
              }
              nativeButton={false}
              variant="outline"
              size="sm"
              className="w-full"
            >
              {maintainers.length > 0
                ? "Also maintain this mod?"
                : "Maintain this mod? Claim it"}
            </Button>
            {slug && (
              <p className="text-muted-foreground text-xs">
                Verified through push access to {slug}.
              </p>
            )}
          </div>

          <p className="text-muted-foreground border-border border-t pt-4 text-xs">
            Listing details come from the community content index. To change
            them, open a pull request against{" "}
            <a
              className="underline underline-offset-4"
              href="https://github.com/KSAModding/content-index"
              target="_blank"
              rel="noreferrer noopener"
            >
              KSAModding/content-index
            </a>{" "}
            for id <code>{listing.id}</code>.
          </p>
        </aside>
      </div>
    </main>
  );
}
