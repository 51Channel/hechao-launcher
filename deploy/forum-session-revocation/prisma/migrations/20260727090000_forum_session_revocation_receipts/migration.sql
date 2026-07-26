CREATE TABLE "ForumSessionRevocationReceipt" (
    "requestId" TEXT NOT NULL PRIMARY KEY,
    "launcherAccountId" TEXT NOT NULL,
    "createdAt" DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX "ForumSessionRevocationReceipt_launcherAccountId_createdAt_idx"
    ON "ForumSessionRevocationReceipt"("launcherAccountId", "createdAt");
