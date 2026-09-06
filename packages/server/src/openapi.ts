import { OpenAPIGenerator } from '@orpc/openapi'
import { ZodToJsonSchemaConverter } from '@orpc/zod/zod4'
import { auth } from './auth'
import { router } from './router'

const generator = new OpenAPIGenerator({
  // The router's inputs and outputs are zod 4 schemas, so they can be turned
  // into JSON Schema rather than documented as opaque objects.
  schemaConverters: [new ZodToJsonSchemaConverter()],
})

const baseURL = process.env.BETTER_AUTH_URL ?? 'http://localhost:3000'

/**
 * The OpenAPI document for the oRPC router. Generated from the same procedures
 * the server actually serves, so it cannot drift from the implementation.
 *
 * The router is fixed at startup, so the document is built once and reused.
 */
let cached: Promise<unknown> | undefined

/**
 * Router paths that stay out of the published document. The procedures still
 * exist and are still served at /v1; they are simply not advertised.
 * Authorization is enforced by adminOnly, never by hiding them.
 */
const isAdminProcedure = (path: readonly string[]) => path.includes('admin')

export function generateOpenAPIDocument() {
  cached ??= generator.generate(router, {
    filter: ({ path }) => !isAdminProcedure(path),
    info: {
      title: 'ksamods.gg API',
      version: '0.1.0',
      description:
        'Content index and listing maintainership. The same procedures are also available over the oRPC protocol at /rpc.',
    },
    servers: [{ url: `${baseURL}/v1` }],
    security: [{ sessionCookie: [] }],
    components: {
      securitySchemes: {
        sessionCookie: {
          type: 'apiKey',
          in: 'cookie',
          name: 'better-auth.session_token',
          description: 'Session cookie issued by the auth routes under /auth.',
        },
      },
    },
  })
  return cached
}

/**
 * The auth routes document, with the admin plugin's routes removed. Better
 * Auth's openAPI plugin cannot filter, so the filtering happens here and its
 * bundled reference page is disabled in auth.ts.
 *
 * Not cached: it is built from the live auth options and is not on a hot path.
 */
export async function generateAuthOpenAPIDocument() {
  const schema = (await auth.api.generateOpenAPISchema()) as {
    paths: Record<string, unknown>
  }

  return {
    ...schema,
    paths: Object.fromEntries(
      Object.entries(schema.paths).filter(([path]) => !path.startsWith('/admin/')),
    ),
  }
}
