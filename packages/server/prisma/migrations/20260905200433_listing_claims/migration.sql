-- CreateEnum
CREATE TYPE "ListingClaimStatus" AS ENUM ('pending', 'approved', 'rejected', 'revoked');

-- AlterTable
ALTER TABLE "session" ADD COLUMN     "impersonatedBy" TEXT;

-- AlterTable
ALTER TABLE "user" ADD COLUMN     "banExpires" TIMESTAMP(3),
ADD COLUMN     "banReason" TEXT,
ADD COLUMN     "banned" BOOLEAN DEFAULT false,
ADD COLUMN     "role" TEXT;

-- CreateTable
CREATE TABLE "listing_claim" (
    "id" TEXT NOT NULL,
    "listingId" TEXT NOT NULL,
    "userId" TEXT NOT NULL,
    "status" "ListingClaimStatus" NOT NULL DEFAULT 'pending',
    "evidence" JSONB NOT NULL DEFAULT '[]',
    "note" TEXT,
    "decidedAt" TIMESTAMP(3),
    "decidedById" TEXT,
    "createdAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updatedAt" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "listing_claim_pkey" PRIMARY KEY ("id")
);

-- CreateIndex
CREATE INDEX "listing_claim_listingId_status_idx" ON "listing_claim"("listingId", "status");

-- CreateIndex
CREATE INDEX "listing_claim_userId_idx" ON "listing_claim"("userId");

-- CreateIndex
CREATE UNIQUE INDEX "listing_claim_listingId_userId_key" ON "listing_claim"("listingId", "userId");

-- AddForeignKey
ALTER TABLE "listing_claim" ADD CONSTRAINT "listing_claim_userId_fkey" FOREIGN KEY ("userId") REFERENCES "user"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "listing_claim" ADD CONSTRAINT "listing_claim_decidedById_fkey" FOREIGN KEY ("decidedById") REFERENCES "user"("id") ON DELETE SET NULL ON UPDATE CASCADE;
