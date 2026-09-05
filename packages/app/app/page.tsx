import Link from "next/link";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { SITE_CONTAINER } from "@/lib/layout";
import { orpc } from "@/lib/orpc";

// The snapshot changes when the worker pulls, so render per request rather than
// baking a copy in at build time.
export const dynamic = "force-dynamic";

export default async function Home() {
  // Counts only. The listings themselves live on the mods page.
  const snapshot = await orpc.contentIndex.latest().catch(() => null);
  const listings = snapshot?.data.listings ?? [];
  const releaseCount = listings.reduce(
    (total, listing) => total + listing.releases.length,
    0,
  );
  const gameVersions = snapshot?.data.game_versions?.versions ?? [];
  const latestGameVersion = gameVersions[gameVersions.length - 1];

  return (
    <main className={`${SITE_CONTAINER} flex-1 py-16`}>
      <section className="flex flex-col items-start gap-6 py-12">
        <Badge variant="secondary">Community mod index</Badge>
        <h1 className="max-w-2xl text-4xl font-semibold tracking-tight text-balance sm:text-5xl">
          Mods for Kitten Space Agency
        </h1>
        <p className="text-muted-foreground max-w-xl text-lg text-pretty">
          Browse every mod in the community content index, with verified
          downloads, version requirements and dependencies kept in sync
          automatically.
        </p>
        <div className="flex flex-wrap gap-3">
          <Button render={<Link href="/mods" />} nativeButton={false} size="lg">
            Browse mods
          </Button>
          <Button
            render={<Link href="/sign-in" />}
            nativeButton={false}
            variant="outline"
            size="lg"
          >
            Create an account
          </Button>
        </div>
      </section>

      <section className="border-border grid grid-cols-2 gap-6 border-y py-8 sm:grid-cols-3">
        <div>
          <div className="text-2xl font-semibold">{listings.length}</div>
          <div className="text-muted-foreground text-sm">Listings indexed</div>
        </div>
        <div>
          <div className="text-2xl font-semibold">{releaseCount}</div>
          <div className="text-muted-foreground text-sm">Releases tracked</div>
        </div>
        <div>
          <div className="truncate text-2xl font-semibold">
            {latestGameVersion ?? "Unknown"}
          </div>
          <div className="text-muted-foreground text-sm">Latest game build</div>
        </div>
      </section>

      <section className="grid gap-8 py-12 sm:grid-cols-3">
        <div className="space-y-2">
          <h2 className="font-medium">Always current</h2>
          <p className="text-muted-foreground text-sm text-pretty">
            A worker pulls the published index on a schedule, so versions and
            download links stay in step with upstream.
          </p>
        </div>
        <div className="space-y-2">
          <h2 className="font-medium">Verified downloads</h2>
          <p className="text-muted-foreground text-sm text-pretty">
            Every release carries a checksum and a size, taken straight from the
            index rather than re-hosted.
          </p>
        </div>
        <div className="space-y-2">
          <h2 className="font-medium">Compatibility you can filter</h2>
          <p className="text-muted-foreground text-sm text-pretty">
            Each release records the game builds it supports, so you can narrow
            the list to your install.
          </p>
        </div>
      </section>

      <footer className="border-border text-muted-foreground border-t py-8 text-sm">
        <p>
          Data from the{" "}
          <a
            className="underline underline-offset-4"
            href="https://github.com/KSAModding/content-index"
            target="_blank"
            rel="noreferrer noopener"
          >
            community content index
          </a>
          {snapshot
            ? `, synced ${new Date(snapshot.fetchedAt).toUTCString()}.`
            : "."}
        </p>
      </footer>
    </main>
  );
}
