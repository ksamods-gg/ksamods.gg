-- CreateTable
CREATE TABLE "mod" (
    "id" TEXT NOT NULL,
    "listingId" TEXT NOT NULL,
    "submissionId" TEXT,
    "createdAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updatedAt" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "mod_pkey" PRIMARY KEY ("id")
);

-- CreateIndex
CREATE UNIQUE INDEX "mod_listingId_key" ON "mod"("listingId");

-- CreateIndex
CREATE UNIQUE INDEX "mod_submissionId_key" ON "mod"("submissionId");
