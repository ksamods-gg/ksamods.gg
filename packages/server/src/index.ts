import { RPCHandler } from '@orpc/server/fetch'
import { Hono } from 'hono'
import { cors } from 'hono/cors'
import { auth } from './auth'
import { startContentIndexWorker } from './content-index'
import { router } from './router'

const app = new Hono()

// bun --hot re-evaluates this module on save; the guard keeps one timer.
if (!(globalThis as any).__contentIndexWorker) {
  ;(globalThis as any).__contentIndexWorker = startContentIndexWorker()
}
const rpc = new RPCHandler(router)

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

app.use('/rpc/*', async (c, next) => {
  const { matched, response } = await rpc.handle(c.req.raw, {
    prefix: '/rpc',
    context: { headers: c.req.raw.headers },
  })
  return matched ? response : next()
})

export default app
