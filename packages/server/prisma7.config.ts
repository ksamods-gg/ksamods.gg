import { defineConfig } from "prisma/config";

// The Prisma CLI runs this under Node, not Bun, so .env is not auto-loaded.
try {
  process.loadEnvFile();
} catch {
  // no .env — assume DATABASE_URL comes from the real environment
}

export default defineConfig({
  schema: "prisma/schema.prisma",
  migrations: {
    path: "prisma/migrations",
  },
  datasource: {
    url: process.env["DATABASE_URL"],
  },
});
