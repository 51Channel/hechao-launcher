import { createHash, timingSafeEqual } from "node:crypto";
import { prisma } from "@/lib/db";

export const runtime = "nodejs";

const UUID_PATTERN =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

function isAuthorized(request: Request): boolean {
  const expected = process.env.HECHAO_SESSION_REVOCATION_TOKEN?.trim() ?? "";
  const provided =
    request.headers.get("x-hechao-session-token")?.trim() ?? "";
  if (
    expected.length < 32 ||
    expected.length > 256 ||
    provided.length < 32 ||
    provided.length > 256
  ) {
    return false;
  }

  const expectedDigest = createHash("sha256").update(expected).digest();
  const providedDigest = createHash("sha256").update(provided).digest();
  return timingSafeEqual(expectedDigest, providedDigest);
}

export async function POST(request: Request) {
  if (!isAuthorized(request)) {
    return new Response(null, { status: 404 });
  }

  const body = (await request.json().catch(() => null)) as {
    requestId?: unknown;
    userId?: unknown;
  } | null;
  const requestId =
    typeof body?.requestId === "string" ? body.requestId.trim() : "";
  const userId = typeof body?.userId === "string" ? body.userId.trim() : "";
  if (!UUID_PATTERN.test(requestId) || !UUID_PATTERN.test(userId)) {
    return Response.json(
      { error: "requestId 或 userId 无效" },
      { status: 400 },
    );
  }

  await prisma.$transaction(async (transaction) => {
    const inserted = await transaction.$executeRaw`
      INSERT INTO "ForumSessionRevocationReceipt"
          ("requestId", "launcherAccountId")
      VALUES (${requestId}, ${userId})
      ON CONFLICT ("requestId") DO NOTHING
    `;
    if (inserted !== 1) return;

    await transaction.$executeRaw`
      UPDATE "User"
      SET "sessionVersion" = "sessionVersion" + 1
      WHERE "launcherAccountId" = ${userId}
    `;
  });

  return new Response(null, { status: 204 });
}
