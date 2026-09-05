import Link from "next/link";
import { Button } from "@/components/ui/button";
import { SITE_CONTAINER } from "@/lib/layout";

export default function ListingNotFound() {
  return (
    <main className={`${SITE_CONTAINER} flex-1 py-10`}>
      <div className="border-border text-muted-foreground space-y-4 rounded-lg border border-dashed py-16 text-center text-sm">
        <p>That listing is not in the current content index.</p>
        <Button
          render={<Link href="/mods" />}
          nativeButton={false}
          variant="outline"
          size="sm"
        >
          Back to mods
        </Button>
      </div>
    </main>
  );
}
