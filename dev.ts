// ponytail: `bun run --filter '*' dev` silently never starts `next dev` alongside
// another long-running script. Spawning with inherited stdio works; drop this
// file for --filter once that's fixed upstream.
const procs = ['packages/server', 'packages/app'].map((cwd) =>
  Bun.spawn(['bun', 'run', 'dev'], { cwd, stdio: ['inherit', 'inherit', 'inherit'] }),
)

const kill = () => procs.forEach((p) => p.kill())
process.on('SIGINT', kill)
process.on('exit', kill)

await Promise.all(procs.map((p) => p.exited))
