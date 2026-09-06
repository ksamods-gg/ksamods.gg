import { expect, test } from "bun:test";
import { adminAccess } from "./admin-access";

test("a failed check is never reported as a denial", () => {
  // The bug this exists to stop: an unreachable API rendering as "you do not
  // have access", which reads as a permissions problem and is not one.
  expect(adminAccess(false, true, "failed")).toBe("unreachable");
  expect(adminAccess(false, true, "failed")).not.toBe("denied");
});

test.each([
  ["still loading the session", true, false, "pending", "checking"],
  ["signed out", false, false, "pending", "signed-out"],
  ["signed out even once the check fails", false, false, "failed", "signed-out"],
  ["signed in, check not back yet", false, true, "pending", "checking"],
  ["an admin", false, true, "admin", "allowed"],
  ["not an admin", false, true, "not-admin", "denied"],
] as const)("%s", (_label, pending, signedIn, check, expected) => {
  expect(adminAccess(pending, signedIn, check)).toBe(expected);
});
