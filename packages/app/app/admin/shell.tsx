"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { cn } from "cn";
import { Button } from "@/components/ui/button";
import { type AdminCheck, adminAccess } from "@/lib/admin-access";
import { useSession } from "@/lib/auth-client";
import { orpc } from "@/lib/orpc-browser";

const SECTIONS = [
  { href: "/admin/users", label: "Users" },
  { href: "/admin/claims", label: "Claims" },
  { href: "/admin/links", label: "Link issues" },
] as const;

export function AdminShell({ children }: { children: React.ReactNode }) {
  const { data: session, isPending } = useSession();
  const [check, setCheck] = useState<AdminCheck>("pending");

  useEffect(() => {
    if (!session) return;

    // Asked from the browser, exactly like the header's Admin link, so the two
    // can never disagree. A server component check can be replayed from Next's
    // router cache and go stale against a session that has since changed.
    orpc
      .amIAdmin()
      .then((admin) => setCheck(admin ? "admin" : "not-admin"))
      // amIAdmin answers false for a non-admin, so a rejection is never that.
      .catch(() => setCheck("failed"));
  }, [session]);

  const access = adminAccess(isPending, Boolean(session), check);

  if (access === "checking")
    return <p className="text-muted-foreground text-sm">Checking access...</p>;

  if (access === "signed-out")
    return (
      <div className="space-y-4">
        <p className="text-muted-foreground text-sm">
          Sign in to open the admin dashboard.
        </p>
        <Button render={<Link href="/sign-in" />} nativeButton={false}>
          Sign in
        </Button>
      </div>
    );

  if (access === "denied")
    return (
      <p className="text-muted-foreground text-sm">
        You do not have access to this page.
      </p>
    );

  if (access === "unreachable")
    return (
      <div className="space-y-2">
        <p role="alert" className="text-destructive text-sm">
          Could not check your access.
        </p>
        <p className="text-muted-foreground text-sm">
          The API did not answer. This is not a permissions problem: check that
          the server is running and that the site is pointed at it.
        </p>
      </div>
    );

  return (
    <div className="flex flex-col gap-8 md:flex-row md:gap-10">
      <AdminNav />
      {/* min-w-0 so a wide table scrolls inside the column instead of pushing
          the whole page sideways. */}
      <div className="min-w-0 flex-1">{children}</div>
    </div>
  );
}

function AdminNav() {
  const pathname = usePathname();
  const [issues, setIssues] = useState(0);

  useEffect(() => {
    orpc.listings.admin
      .linkIssues()
      .then((rows) => setIssues(rows.length))
      .catch(() => setIssues(0));
  }, []);

  return (
    // A row that scrolls on small screens, a column beside the content above it.
    <nav
      aria-label="Admin sections"
      className="-mx-6 flex shrink-0 gap-1 overflow-x-auto px-6 md:mx-0 md:w-48 md:flex-col md:overflow-visible md:px-0"
    >
      {SECTIONS.map((section) => {
        const active = pathname === section.href;
        return (
          <Link
            key={section.href}
            href={section.href}
            aria-current={active ? "page" : undefined}
            className={cn(
              "flex items-center justify-between gap-2 rounded-md px-3 py-2 text-sm whitespace-nowrap transition-colors",
              active
                ? "bg-muted text-foreground font-medium"
                : "text-muted-foreground hover:bg-muted/60 hover:text-foreground",
            )}
          >
            {section.label}
            {section.href === "/admin/links" && issues > 0 && (
              <span className="bg-destructive text-destructive-foreground inline-flex h-5 min-w-5 items-center justify-center rounded-full px-1.5 text-xs font-medium">
                {issues}
              </span>
            )}
          </Link>
        );
      })}
    </nav>
  );
}
