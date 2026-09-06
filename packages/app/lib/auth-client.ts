import { createAuthClient } from "better-auth/react";
import { steamOpenIDClient } from "better-auth-steam/client";

export const authClient = createAuthClient({
  baseURL: process.env.NEXT_PUBLIC_SERVER_URL ?? "http://localhost:3000",
  // Must match basePath in the server's auth.ts.
  basePath: "/auth",
  plugins: [steamOpenIDClient()],
});

export const { signIn, signUp, signOut, useSession } = authClient;
