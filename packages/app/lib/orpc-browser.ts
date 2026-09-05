import { createORPCClient } from "@orpc/client";
import { RPCLink } from "@orpc/client/fetch";
import type { Client } from "server/router";

/**
 * Browser client. The API is a different origin, so the auth cookie only rides
 * along when credentials are included. Use lib/orpc.ts on the server.
 */
const link = new RPCLink({
  url: `${process.env.NEXT_PUBLIC_SERVER_URL ?? "http://localhost:3000"}/rpc`,
  fetch: (request, init) =>
    globalThis.fetch(request, { ...init, credentials: "include" }),
});

export const orpc: Client = createORPCClient(link);
