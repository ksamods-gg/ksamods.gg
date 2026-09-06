import { SITE_CONTAINER } from "@/lib/layout";
import { AdminShell } from "./shell";

export const metadata = { title: "Admin | ksamods.gg" };

export default function AdminLayout({ children }: LayoutProps<"/admin">) {
  return (
    <main className={`${SITE_CONTAINER} flex-1 py-10`}>
      <AdminShell>{children}</AdminShell>
    </main>
  );
}
