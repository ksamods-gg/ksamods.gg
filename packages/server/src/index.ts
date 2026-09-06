import { swaggerUI } from '@hono/swagger-ui'
import { OpenAPIHandler } from '@orpc/openapi/fetch'
import { RPCHandler } from '@orpc/server/fetch'
import { Hono } from 'hono'
import { cors } from 'hono/cors'
import { auth } from './auth'
import { startContentIndexWorker } from './content-index'
import { generateAuthOpenAPIDocument, generateOpenAPIDocument } from './openapi'
import { router } from './router'

const app = new Hono()

// bun --hot re-evaluates this module on save; the guard keeps one timer.
if (!(globalThis as any).__contentIndexWorker) {
  ;(globalThis as any).__contentIndexWorker = startContentIndexWorker()
}
const rpc = new RPCHandler(router)
// The same router, served as REST. /api/v1 rather than /api so it cannot
// collide with the auth routes mounted under /api/auth.
const openapi = new OpenAPIHandler(router)

// The app runs on a different origin, so auth cookies need credentialed CORS.
app.use(
  '*',
  cors({
    origin: process.env.APP_URL ?? 'http://localhost:3001',
    credentials: true,
    allowHeaders: ['Content-Type', 'Authorization'],
    allowMethods: ['GET', 'POST', 'OPTIONS'],
  }),
)

app.on(['GET', 'POST'], '/api/auth/*', (c) => auth.handler(c.req.raw))

app.get('/', (c) => {
  return c.text('Hello Hono!')
})

app.get('/openapi.json', async (c) => c.json((await generateOpenAPIDocument()) as object))
app.get('/auth-openapi.json', async (c) => c.json(await generateAuthOpenAPIDocument()))

// Swagger UI, served from a CDN so the server ships no UI assets. The major
// is pinned: the documents are OpenAPI 3.1, which needs Swagger UI 5, and an
// unpinned CDN would silently follow a future major.
const SWAGGER_UI_VERSION = '5'

app.get(
  '/docs',
  swaggerUI({ url: '/openapi.json', title: 'ksamods.gg API', version: SWAGGER_UI_VERSION }),
)
app.get(
  '/docs/auth',
  swaggerUI({
    url: '/auth-openapi.json',
    title: 'ksamods.gg auth API',
    version: SWAGGER_UI_VERSION,
  }),
)

app.use('/api/v1/*', async (c, next) => {
  const { matched, response } = await openapi.handle(c.req.raw, {
    prefix: '/api/v1',
    context: { headers: c.req.raw.headers },
  })
  return matched ? response : next()
})

app.use('/rpc/*', async (c, next) => {
  const { matched, response } = await rpc.handle(c.req.raw, {
    prefix: '/rpc',
    context: { headers: c.req.raw.headers },
  })
  return matched ? response : next()
})

export default app
