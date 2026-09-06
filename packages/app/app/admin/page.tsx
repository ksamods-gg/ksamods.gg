import { redirect } from "next/navigation";

// /admin is the dashboard's front door, not a page of its own.
export default function AdminIndexPage() {
  redirect("/admin/users");
}
