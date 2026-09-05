import { SITE_CONTAINER } from "@/lib/layout";
import { orpc } from "@/lib/orpc";
import { ModBrowser } from "./mod-browser";

// The snapshot changes when the worker pulls, so render per request rather than
// baking a copy in at build time.
export const dynamic = "force-dynamic";

export const metadata = {
  title: "Mods | ksamods.gg",
  description: "Browse the Kitten Space Agency community content index.",
};

export default async function ModsPage() {
  const snapshot = await orpc.contentIndex.latest().catch(() => null);
  const listings = snapshot?.data.listings ?? [];
  // Newest first, matching how a version picker is normally read.
  const gameVersions = [
    ...(snapshot?.data.game_versions?.versions ?? []),
  ].reverse();

  return (
    <main className={`${SITE_CONTAINER} flex-1 py-10`}>
      <div className="mb-8 space-y-2">
        <h1 className="text-3xl font-semibold tracking-tight">Mods</h1>
        <p className="text-muted-foreground">
          Every listing in the community content index, with verified downloads
          and version requirements.
        </p>
      </div>

      {snapshot ? (
        <ModBrowser listings={listings} gameVersions={gameVersions} />
      ) : (
        <div className="border-border text-muted-foreground rounded-lg border border-dashed py-16 text-center text-sm">
          The content index is not available right now. Try again in a moment.
        </div>
      )}
    </main>
  );
}
