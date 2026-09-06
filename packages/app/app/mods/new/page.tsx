import { SITE_CONTAINER } from "@/lib/layout";
import { orpc } from "@/lib/orpc";
import { SubmitForm } from "./submit-form";

// The snapshot decides which ids are taken and which game builds exist, and it
// changes when the worker pulls.
export const dynamic = "force-dynamic";

export const metadata = {
  title: "Submit a mod | ksamods.gg",
  description:
    "Add your mod to the Kitten Space Agency community content index.",
};

export default async function NewListingPage() {
  const snapshot = await orpc.contentIndex.latest().catch(() => null);

  const takenIds = snapshot?.data.listings.map((listing) => listing.id) ?? [];
  // Newest first, matching how a version picker is normally read.
  const gameVersions = [
    ...(snapshot?.data.game_versions?.versions ?? []),
  ].reverse();

  return (
    <main className={`${SITE_CONTAINER} flex-1 py-10`}>
      <div className="mx-auto max-w-3xl space-y-8">
        <div className="space-y-2">
          <h1 className="text-3xl font-semibold tracking-tight">
            Submit a mod
          </h1>
          <p className="text-muted-foreground text-pretty">
            This opens a pull request on the community content index as you, on
            your own GitHub account. Once it is verified and merged, your mod
            appears here.
          </p>
        </div>

        <SubmitForm takenIds={takenIds} gameVersions={gameVersions} />
      </div>
    </main>
  );
}
