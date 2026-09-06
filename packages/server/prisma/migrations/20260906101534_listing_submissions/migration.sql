-- CreateEnum
CREATE TYPE "ListingSubmissionState" AS ENUM ('submitting', 'open', 'merged', 'closed', 'failed');

-- CreateTable
CREATE TABLE "listing_submission" (
    "id" TEXT NOT NULL,
    "listingId" TEXT NOT NULL,
    "userId" TEXT NOT NULL,
    "state" "ListingSubmissionState" NOT NULL DEFAULT 'submitting',
    "toml" TEXT NOT NULL,
    "document" JSONB NOT NULL,
    "githubLogin" TEXT NOT NULL,
    "githubUserId" TEXT NOT NULL,
    "baseRepo" TEXT NOT NULL,
    "baseBranch" TEXT NOT NULL,
    "headRepo" TEXT,
    "headBranch" TEXT,
    "commitSha" TEXT,
    "prNumber" INTEGER,
    "prUrl" TEXT,
    "ownershipProof" TEXT,
    "acknowledged" BOOLEAN NOT NULL DEFAULT false,
    "events" JSONB NOT NULL DEFAULT '[]',
    "attempts" INTEGER NOT NULL DEFAULT 0,
    "error" TEXT,
    "refreshedAt" TIMESTAMP(3),
    "createdAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updatedAt" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "listing_submission_pkey" PRIMARY KEY ("id")
);

-- CreateIndex
CREATE INDEX "listing_submission_listingId_state_idx" ON "listing_submission"("listingId", "state");

-- CreateIndex
CREATE INDEX "listing_submission_userId_createdAt_idx" ON "listing_submission"("userId", "createdAt");

-- CreateIndex
CREATE UNIQUE INDEX "listing_submission_listingId_userId_key" ON "listing_submission"("listingId", "userId");

-- AddForeignKey
ALTER TABLE "listing_submission" ADD CONSTRAINT "listing_submission_userId_fkey" FOREIGN KEY ("userId") REFERENCES "user"("id") ON DELETE CASCADE ON UPDATE CASCADE;
