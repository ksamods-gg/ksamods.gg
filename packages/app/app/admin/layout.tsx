import { SITE_CONTAINER } from "@/lib/layout";
import { orpc } from "@/lib/orpc";
import { AdminNav } from "./nav";

// Reads the caller's session, so it can never be a build-time render.
export const dynamic = "force-dynamic";

export const metadata = { title: "Admin | ksamods.gg" };

export default async function AdminLayout({ children }: LayoutProps<"/admin">) {
  // Cosmetic gate. Every procedure behind this page is adminOnly, so a
  // non-admin who navigates here anyway sees an empty page, not data.
  const admin = await orpc.amIAdmin().catch(() => false);

  if (!admin)
    return (
      <main className={`${SITE_CONTAINER} flex-1 py-10`}>
        <p className="text-muted-foreground text-sm">
          You do not have access to this page.
        </p>
      </main>
    );

  return (
    <main className={`${SITE_CONTAINER} flex-1 py-10`}>
      <div className="flex flex-col gap-8 md:flex-row md:gap-10">
        <AdminNav />
        {/* min-w-0 so a wide table scrolls inside the column instead of
            pushing the whole page sideways. */}
        <div className="min-w-0 flex-1">{children}</div>
      </div>
    </main>
  );
}
