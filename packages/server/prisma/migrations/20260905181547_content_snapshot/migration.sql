-- CreateTable
CREATE TABLE "content_snapshot" (
    "id" TEXT NOT NULL,
    "snapshotVersion" INTEGER NOT NULL,
    "authoredCommit" TEXT NOT NULL,
    "generatedCommit" TEXT NOT NULL,
    "etag" TEXT,
    "listingCount" INTEGER NOT NULL,
    "data" JSONB NOT NULL,
    "fetchedAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "createdAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "content_snapshot_pkey" PRIMARY KEY ("id")
);

-- CreateIndex
CREATE UNIQUE INDEX "content_snapshot_generatedCommit_key" ON "content_snapshot"("generatedCommit");

-- CreateIndex
CREATE INDEX "content_snapshot_fetchedAt_idx" ON "content_snapshot"("fetchedAt");
