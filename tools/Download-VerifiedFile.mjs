import { createHash } from "node:crypto";
import { createReadStream } from "node:fs";
import { mkdir, open, rm, stat } from "node:fs/promises";
import { dirname } from "node:path";
import { Readable } from "node:stream";
import { pipeline } from "node:stream/promises";

const [url, outputPath, expectedBytesText, expectedSha256Text, partCountText = "8"] =
  process.argv.slice(2);

if (!url || !outputPath || !expectedBytesText || !expectedSha256Text) {
  throw new Error(
    "Usage: node Download-VerifiedFile.mjs <url> <output> <bytes> <sha256> [parts]",
  );
}

const expectedBytes = Number(expectedBytesText);
const expectedSha256 = expectedSha256Text.toUpperCase();
const partCount = Number(partCountText);

if (!Number.isSafeInteger(expectedBytes) || expectedBytes <= 0) {
  throw new Error("Expected byte count must be a positive safe integer.");
}

if (!Number.isInteger(partCount) || partCount < 1 || partCount > 32) {
  throw new Error("Part count must be between 1 and 32.");
}

async function sha256(path) {
  const hash = createHash("sha256");
  for await (const chunk of createReadStream(path)) {
    hash.update(chunk);
  }
  return hash.digest("hex").toUpperCase();
}

async function validFile(path, bytes, digest) {
  try {
    const file = await stat(path);
    return file.size === bytes && (await sha256(path)) === digest;
  } catch {
    return false;
  }
}

async function downloadPart(index, start, end) {
  const path = `${outputPath}.part-${String(index).padStart(2, "0")}`;
  const expectedPartBytes = end - start + 1;

  try {
    const file = await stat(path);
    if (file.size === expectedPartBytes) {
      return path;
    }
  } catch {
    // The part is not present yet.
  }

  await rm(path, { force: true });

  for (let attempt = 1; attempt <= 5; attempt += 1) {
    try {
      const response = await fetch(url, {
        headers: {
          Range: `bytes=${start}-${end}`,
          "User-Agent": "Hechao-Verified-Downloader/1.0",
        },
        redirect: "follow",
        signal: AbortSignal.timeout(60_000),
      });

      const contentRange = response.headers.get("content-range");
      if (
        response.status !== 206 ||
        contentRange !== `bytes ${start}-${end}/${expectedBytes}`
      ) {
        throw new Error(
          `Server rejected range ${start}-${end}: ${response.status} ${contentRange}`,
        );
      }

      await pipeline(
        Readable.fromWeb(response.body),
        (await import("node:fs")).createWriteStream(path),
      );

      const file = await stat(path);
      if (file.size !== expectedPartBytes) {
        throw new Error(
          `Part ${index} size mismatch: expected ${expectedPartBytes}, got ${file.size}`,
        );
      }

      process.stdout.write(`part ${index + 1}/${partCount} complete\n`);
      return path;
    } catch (error) {
      await rm(path, { force: true });
      if (attempt === 5) {
        throw error;
      }
      await new Promise((resolve) => setTimeout(resolve, attempt * 1_000));
    }
  }

  throw new Error(`Part ${index} did not complete.`);
}

await mkdir(dirname(outputPath), { recursive: true });

if (await validFile(outputPath, expectedBytes, expectedSha256)) {
  process.stdout.write(
    JSON.stringify({
      status: "already-verified",
      path: outputPath,
      bytes: expectedBytes,
      sha256: expectedSha256,
    }),
  );
  process.stdout.write("\n");
  process.exit(0);
}

const ranges = Array.from({ length: partCount }, (_, index) => {
  const start = Math.floor((expectedBytes * index) / partCount);
  const end =
    index === partCount - 1
      ? expectedBytes - 1
      : Math.floor((expectedBytes * (index + 1)) / partCount) - 1;
  return { index, start, end };
});

const partPaths = await Promise.all(
  ranges.map(({ index, start, end }) => downloadPart(index, start, end)),
);

const assemblingPath = `${outputPath}.assembling`;
await rm(assemblingPath, { force: true });
const target = await open(assemblingPath, "w");

try {
  for (const partPath of partPaths) {
    const source = await open(partPath, "r");
    try {
      const buffer = Buffer.allocUnsafe(1024 * 1024);
      while (true) {
        const { bytesRead } = await source.read(buffer, 0, buffer.length, null);
        if (bytesRead === 0) {
          break;
        }
        await target.write(buffer, 0, bytesRead);
      }
    } finally {
      await source.close();
    }
  }
} finally {
  await target.close();
}

const actualBytes = (await stat(assemblingPath)).size;
const actualSha256 = await sha256(assemblingPath);
if (actualBytes !== expectedBytes || actualSha256 !== expectedSha256) {
  throw new Error(
    `Verification failed: bytes=${actualBytes}, sha256=${actualSha256}`,
  );
}

await rm(outputPath, { force: true });
await (await import("node:fs/promises")).rename(assemblingPath, outputPath);
await Promise.all(partPaths.map((path) => rm(path, { force: true })));

process.stdout.write(
  JSON.stringify({
    status: "downloaded-and-verified",
    path: outputPath,
    bytes: actualBytes,
    sha256: actualSha256,
  }),
);
process.stdout.write("\n");
