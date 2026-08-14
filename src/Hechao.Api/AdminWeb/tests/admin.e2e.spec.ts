import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Locator, type Page, type Request, type Route } from "@playwright/test";
import { existsSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

const assetRoot = fileURLToPath(new URL("../../wwwroot/admin/assets", import.meta.url));
const hashOne = "1".repeat(64);
const hashTwo = "2".repeat(64);
const migratedRoutes = [
  ["servers", "服务器目录"],
  ["users", "玩家与权限"],
  ["profiles", "客户端档案"],
  ["package-imports", "整合包导入"],
  ["activity-plans", "活动企划"],
  ["telemetry", "运行数据"],
  ["runtime", "服务状态"],
  ["control", "服控面板"],
  ["alerts", "统一告警中心"],
  ["diagnostics", "玩家诊断包"],
  ["audit", "审计记录"]
] as const;

const session = {
  player: {
    userId: "11111111-1111-1111-1111-111111111111",
    minecraftUuid: "22222222-2222-2222-2222-222222222222",
    minecraftName: "HechaoAdmin",
    luckPermsPrimaryGroup: "administrator",
    accessTier: "Administrator",
    luckPermsSyncedAt: "2026-08-02T03:00:00Z"
  },
  mfaConfigured: true,
  mfaVerified: true,
  expiresAt: "2026-08-02T08:00:00Z"
};

function quickSettings(maxPlayers: number) {
  return {
    maxPlayers,
    viewDistance: 10,
    simulationDistance: 8,
    difficulty: "normal",
    whiteList: false,
    initialMemoryMiB: 2048,
    maximumMemoryMiB: 4096,
    maximumAllowedMemoryMiB: 8192
  };
}

interface ControlTargetMock {
  serverId: string;
  displayName: string;
  agentId: string;
  conflictGroup: string | null;
  port: number;
  agentConnected: boolean;
  lastSeenAt: string;
  online: boolean;
  processId: number | null;
  settings: ReturnType<typeof quickSettings> | null;
  activeOperation: null;
  packageDeploymentEnabled: boolean;
  serverDeletionEnabled: boolean;
  serverFilesPresent: boolean;
  deletionCleanupPending: boolean;
  packageDeploymentMemoryGuidance: {
    hostTotalMemoryMiB: number;
    recommendedMinimumMemoryMiB: number;
    recommendedMaximumMemoryMiB: number;
  } | null;
  deployedPackage: {
    importId: string;
    profileId: string;
    version: string;
  } | null;
}

interface ControlOverviewMock {
  generatedAt: string;
  agentFreshnessSeconds: number;
  targets: ControlTargetMock[];
}

interface ControlTargetDetailMock {
  generatedAt: string;
  agentFreshnessSeconds: number;
  target: ControlTargetMock & {
    allowedCommandPrefixes: string[];
    consoleTail: string;
    consoleCapturedAt: string;
  };
  recentOperations: never[];
}

function controlOverview(requestNumber: number): ControlOverviewMock {
  return {
    generatedAt: "2026-08-02T04:00:00Z",
    agentFreshnessSeconds: 45,
    targets: [{
      serverId: "activity",
      displayName: "活动服",
      agentId: "owl5",
      conflictGroup: "owl5-activity-slot",
      port: 25568,
      agentConnected: true,
      lastSeenAt: "2026-08-02T04:00:00Z",
      online: true,
      processId: 18888,
      settings: quickSettings(requestNumber > 1 ? 99 : 30),
      activeOperation: null,
      packageDeploymentEnabled: true,
      serverDeletionEnabled: true,
      serverFilesPresent: true,
      deletionCleanupPending: false,
      packageDeploymentMemoryGuidance: {
        hostTotalMemoryMiB: 32768,
        recommendedMinimumMemoryMiB: 4096,
        recommendedMaximumMemoryMiB: 16384
      },
      deployedPackage: {
        importId: "66666666-6666-6666-6666-666666666666",
        profileId: "summer-neoforge-1.21.11",
        version: "1.0.0"
      }
    }, {
      serverId: "fanstreet",
      displayName: "范街活动服",
      agentId: "owl5",
      conflictGroup: "owl5-activity-slot",
      port: 25568,
      agentConnected: true,
      lastSeenAt: "2026-08-02T04:00:00Z",
      online: false,
      processId: null,
      settings: quickSettings(40),
      activeOperation: null,
      packageDeploymentEnabled: false,
      serverDeletionEnabled: true,
      serverFilesPresent: true,
      deletionCleanupPending: false,
      packageDeploymentMemoryGuidance: null,
      deployedPackage: null
    }]
  };
}

function controlDetail(requestNumber: number): ControlTargetDetailMock {
  const lines = Array.from({ length: 80 }, (_, index) =>
    `[04:${String(index).padStart(2, "0")}:00 INFO] ${requestNumber > 1 ? "refreshed" : "initial"} log line ${index}`
  );
  return {
    generatedAt: "2026-08-02T04:00:00Z",
    agentFreshnessSeconds: 45,
    target: {
      ...controlOverview(requestNumber).targets[0],
      allowedCommandPrefixes: ["list", "save-all", "whitelist"],
      consoleTail: lines.join("\n"),
      consoleCapturedAt: "2026-08-02T04:00:00Z"
    },
    recentOperations: []
  };
}

const profileSummary = {
  id: "activity-neoforge-1.21.11",
  displayName: "活动服 NeoForge",
  version: "1.0.0",
  downloadBytes: 524288000,
  sha256: hashOne,
  publishedAt: "2026-08-01T08:00:00Z",
  isActive: true,
  isArchived: false,
  archivedAt: null,
  archiveReason: "",
  serverReferenceCount: 2,
  canDelete: false,
  updatedAt: "2026-08-01T08:00:00Z",
  revision: 4,
  releaseCount: 2,
  channels: [
    { channel: "Test", manifestSha256: hashTwo, version: "1.1.0", rolloutPercentage: 100, revision: 2, updatedAt: "2026-08-01T08:00:00Z" },
    { channel: "Gray", manifestSha256: null, version: null, rolloutPercentage: 10, revision: 1, updatedAt: "2026-08-01T08:00:00Z" },
    { channel: "Production", manifestSha256: hashOne, version: "1.0.0", rolloutPercentage: 100, revision: 3, updatedAt: "2026-08-01T08:00:00Z" }
  ]
};

const releases = [
  { profileId: profileSummary.id, manifestSha256: hashTwo, version: "1.1.0", downloadBytes: 530000000, fileCount: 9200, minecraftVersion: "1.21.11", javaVersion: "25", loader: "NeoForge", loaderVersion: "21.11.42", publishedAt: "2026-08-02T01:00:00Z", isPaused: false, pauseReason: "", revision: 1, createdAt: "2026-08-02T01:00:00Z", createdByDisplayName: "HechaoAdmin" },
  { profileId: profileSummary.id, manifestSha256: hashOne, version: "1.0.0", downloadBytes: 524288000, fileCount: 9100, minecraftVersion: "1.21.11", javaVersion: "25", loader: "NeoForge", loaderVersion: "21.11.42", publishedAt: "2026-08-01T08:00:00Z", isPaused: false, pauseReason: "", revision: 1, createdAt: "2026-08-01T08:00:00Z", createdByDisplayName: "HechaoAdmin" }
];

const now = "2026-08-02T04:00:00Z";
const packageImportId = "66666666-6666-6666-6666-666666666666";
const packageAnalysis = {
  layout: "Canonical",
  metadata: {
    suggestedProfileId: "summer-neoforge-1.21.11",
    displayName: "夏日活动",
    version: "1.0.0",
    minecraftVersion: "1.21.11",
    javaMajorVersion: 25,
    loader: "NeoForge",
    loaderVersion: "21.11.42",
    maximumPlayers: 30,
    serverLaunchPath: "start.bat"
  },
  client: { sha256: hashOne, archiveBytes: 524288, expandedBytes: 1048576, fileCount: 24 },
  server: { sha256: hashTwo, archiveBytes: 262144, expandedBytes: 786432, fileCount: 18 },
  clientFileCount: 20,
  serverFileCount: 14,
  sharedFileCount: 4,
  fileSamples: [{ path: "mods/example.jar", side: "Shared", size: 4096, sha256: hashOne }],
  issues: []
};
const completedPackageImport = {
  importId: packageImportId,
  fileName: "summer.zip",
  expectedUploadBytes: 1048576,
  uploadedBytes: 1048576,
  sourceSha256: hashOne,
  status: "Completed",
  analysis: packageAnalysis,
  plan: {
    profileId: "summer-neoforge-1.21.11",
    profileDisplayName: "夏日活动",
    version: "1.0.0",
    targetServerId: "activity",
    preserveWorldData: false,
    syncServerCatalog: true,
    serverDisplayName: "夏日活动",
    minimumTier: "Participant",
    maximumMemoryMiB: 4096,
    deployServer: false
  },
  manifestSha256: hashTwo,
  deploymentOperationId: "77777777-7777-7777-7777-777777777777",
  errorCode: null,
  errorMessage: null,
  createdBy: session.player.userId,
  createdByDisplayName: session.player.minecraftName,
  createdAt: now,
  updatedAt: now,
  completedAt: now,
  revision: 8
};
type PackageImportMock = Omit<
  typeof completedPackageImport,
  "sourceSha256" | "status" | "analysis" | "plan" | "manifestSha256" |
  "deploymentOperationId" | "completedAt"
> & {
  sourceSha256: string | null;
  status: string;
  analysis: typeof packageAnalysis | null;
  plan: typeof completedPackageImport.plan | null;
  manifestSha256: string | null;
  deploymentOperationId: string | null;
  completedAt: string | null;
};
const activityPlanOverview = {
  generatedAt: now,
  plans: [{
    id: "activity-plan-20260812-a1b2c3d4",
    title: "夏日建筑接力",
    announcement: "提前下载整合包，开放后从启动器进入活动服。",
    opensAt: "2026-08-12T11:00:00Z",
    closesAt: "2026-08-12T14:00:00Z",
    maximumPlayers: 30,
    minimumTier: "Participant",
    packageImportId,
    profileId: "summer-neoforge-1.21.11",
    profileDisplayName: "夏日活动",
    version: "1.0.0",
    minecraftVersion: "1.21.11",
    loader: "NeoForge",
    status: "Published",
    effectiveStatus: "Closed",
    productionReady: true,
    deploymentMatches: true,
    revision: 3,
    createdAt: now,
    updatedAt: now
  }, {
    id: "activity-plan-20260820-e5f6a7b8",
    title: "周末生存挑战",
    announcement: "企划仍在准备中。",
    opensAt: "2026-08-20T11:00:00Z",
    closesAt: "2026-08-20T14:00:00Z",
    maximumPlayers: 24,
    minimumTier: "Member",
    packageImportId,
    profileId: "summer-neoforge-1.21.11",
    profileDisplayName: "夏日活动",
    version: "1.0.0",
    minecraftVersion: "1.21.11",
    loader: "NeoForge",
    status: "Draft",
    effectiveStatus: "Closed",
    productionReady: true,
    deploymentMatches: true,
    revision: 1,
    createdAt: now,
    updatedAt: now
  }],
  packages: [{
    importId: packageImportId,
    profileId: "summer-neoforge-1.21.11",
    profileDisplayName: "夏日活动",
    version: "1.0.0",
    manifestSha256: hashTwo,
    minecraftVersion: "1.21.11",
    loader: "NeoForge",
    loaderVersion: "21.11.42",
    maximumPlayers: 30,
    maximumMemoryMiB: 4096,
    preserveWorldData: false,
    productionReady: true,
    profileArchived: false,
    completedAt: now
  }],
  slot: {
    configured: true,
    agentConnected: true,
    online: false,
    serverFilesPresent: true,
    deployedPackage: {
      importId: packageImportId,
      profileId: "summer-neoforge-1.21.11",
      version: "1.0.0"
    },
    activeOperation: null,
    memoryGuidance: {
      hostTotalMemoryMiB: 32768,
      recommendedMinimumMemoryMiB: 4096,
      recommendedMaximumMemoryMiB: 16384
    }
  }
};
const serverRecords = [{
  id: "activity",
  displayName: "活动服",
  shortName: "活动",
  iconGlyph: "活",
  status: "Online",
  maxPlayers: 30,
  minecraftVersion: "1.21.11",
  loader: "NeoForge",
  minimumTier: "Member",
  clientProfileId: profileSummary.id,
  velocityTarget: "activity",
  allowsProtocolTranslation: false,
  role: "Player",
  monitoringEnabled: true,
  sortOrder: 10,
  isVisible: true,
  announcement: "今晚进行活动测试",
  opensAt: null,
  closesAt: null,
  effectiveStatus: "Online",
  revision: 1,
  createdAt: now,
  updatedAt: now,
  hasControlTarget: true,
  controlTargetFresh: true,
  controlReportedOnline: true,
  controlLastSeenAt: now
}, {
  id: "lobby",
  displayName: "基础设施大厅",
  shortName: "大厅",
  iconGlyph: "厅",
  status: "Closed",
  maxPlayers: 30,
  minecraftVersion: "1.21.11",
  loader: "Paper",
  minimumTier: "Administrator",
  clientProfileId: profileSummary.id,
  velocityTarget: "lobby",
  allowsProtocolTranslation: false,
  role: "Infrastructure",
  monitoringEnabled: true,
  sortOrder: -100,
  isVisible: false,
  announcement: "",
  opensAt: null,
  closesAt: null,
  effectiveStatus: "Closed",
  revision: 5,
  createdAt: now,
  updatedAt: now,
  hasControlTarget: false,
  controlTargetFresh: false,
  controlReportedOnline: null,
  controlLastSeenAt: null
}];

const userSummary = {
  userId: "33333333-3333-3333-3333-333333333333",
  username: "player-one",
  displayName: "测试玩家",
  email: "player@example.test",
  minecraftUuid: "44444444-4444-4444-4444-444444444444",
  minecraftName: "TestPlayer",
  luckPermsPrimaryGroup: "member",
  accessTier: "Member",
  luckPermsSyncedAt: now,
  isDisabled: false,
  isMinecraftIdentityBanned: false,
  activeRuleCount: 1,
  createdAt: now
};

const accessRule = {
  userId: userSummary.userId,
  serverId: "activity",
  decision: "Allow",
  reason: "活动测试",
  expiresAt: null,
  revision: 2,
  createdAt: now,
  updatedAt: now
};

const accessPreview = {
  user: userSummary,
  servers: [{
    serverId: "activity",
    serverDisplayName: "活动服",
    configuredStatus: "Online",
    effectiveStatus: "Online",
    isVisible: true,
    minimumTier: "Member",
    allowed: true,
    reason: "AllowedByRule",
    rule: accessRule
  }]
};

const userSecurity = {
  user: userSummary,
  launcherSessions: [{
    sessionId: "55555555-5555-5555-5555-555555555555",
    createdAt: now,
    lastSeenAt: now,
    refreshExpiresAt: "2026-09-02T04:00:00Z",
    sourceIp: "127.0.0.1"
  }],
  activeAdminSessions: 0,
  pendingAdminTickets: 0,
  pendingVelocityLaunchGrants: 0,
  pendingForumSessionRevocations: 0,
  pendingLuckPermsTierChange: null,
  minecraftIdentityBan: null
};

interface MockOptions {
  onProductionUpdate?: (body: Record<string, unknown>) => void;
  intercept?: (route: Route, request: Request, path: string) => Promise<boolean>;
}

async function mockAdminApi(
  page: Page,
  options: MockOptions = {}
): Promise<void> {
  let controlRequests = 0;
  let controlDetailRequests = 0;
  await page.route("**/admin/assets/**", async route => {
    const relative = decodeURIComponent(new URL(route.request().url()).pathname.split("/admin/assets/")[1] ?? "");
    const path = join(assetRoot, relative);
    if (!relative.includes("..") && existsSync(path)) await route.fulfill({ path });
    else await route.fulfill({ status: 404, body: "not found" });
  });
  await page.route("**/v1/**", async route => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    if (options.intercept && await options.intercept(route, request, path)) return;
    if (path === "/v1/admin-auth/session") {
      await route.fulfill({ json: session });
    } else if (path === "/v1/admin-auth/csrf") {
      await route.fulfill({ json: { requestToken: "e2e-csrf" } });
    } else if (path === "/v1/admin/activity-plans" && request.method() === "GET") {
      await route.fulfill({ json: activityPlanOverview });
    } else if (path === "/v1/admin/package-imports" && request.method() === "GET") {
      await route.fulfill({ json: {
        imports: [completedPackageImport],
        publisherAgentConnected: true,
        publisherAgentLastSeenAt: now
      } });
    } else if (path === `/v1/admin/package-imports/${packageImportId}` && request.method() === "GET") {
      await route.fulfill({ json: completedPackageImport });
    } else if (path === "/v1/admin/server-control/overview") {
      controlRequests += 1;
      await route.fulfill({ json: controlOverview(controlRequests) });
    } else if (path === "/v1/admin/server-control/targets/activity") {
      controlDetailRequests += 1;
      await route.fulfill({ json: controlDetail(controlDetailRequests) });
    } else if (path === "/v1/admin/catalog/client-profiles" && request.method() === "GET") {
      await route.fulfill({ json: [profileSummary] });
    } else if (path === `/v1/admin/catalog/client-profiles/${profileSummary.id}` && request.method() === "GET") {
      await route.fulfill({ json: { profile: profileSummary, releases } });
    } else if (path.endsWith("/channels/Production") && request.method() === "PUT") {
      const body = request.postDataJSON() as Record<string, unknown>;
      options.onProductionUpdate?.(body);
      const updated = {
        ...profileSummary,
        version: "1.1.0",
        sha256: hashTwo,
        channels: profileSummary.channels.map(channel => channel.channel === "Production"
          ? { ...channel, manifestSha256: hashTwo, version: "1.1.0", revision: 4 }
          : channel)
      };
      await route.fulfill({ json: { profile: updated, releases } });
    } else if (path === "/v1/admin/catalog/servers") {
      await route.fulfill({ json: serverRecords });
    } else if (path === "/v1/admin/users") {
      await route.fulfill({ json: [userSummary] });
    } else if (path === `/v1/admin/users/${userSummary.userId}/access-preview`) {
      await route.fulfill({ json: accessPreview });
    } else if (path === `/v1/admin/users/${userSummary.userId}/security`) {
      await route.fulfill({ json: userSecurity });
    } else if (path === "/v1/admin/telemetry/summary") {
      await route.fulfill({ json: {
        from: "2026-08-01T04:00:00Z",
        to: "2026-08-02T04:00:00Z",
        windowHours: Number(url.searchParams.get("hours") ?? 24),
        eventCount: 128,
        uniqueUsers: 12,
        downloads: { attempts: 40, succeeded: 37, failed: 2, canceled: 1, bytes: 1073741824, failureRate: 0.05 },
        launches: { attempts: 72, succeeded: 68, failed: 3, canceled: 1, bytes: 0, failureRate: 3 / 72 },
        launcherVersions: [{ launcherVersion: "0.14.2", users: 12 }],
        profileVersions: [{ profileId: profileSummary.id, profileVersion: "1.0.0", users: 9, events: 44 }],
        failures: [{ type: "Install", failureCode: "NetworkUnavailable", count: 2 }]
      } });
    } else if (path === "/v1/admin/server-runtime/summary") {
      await route.fulfill({ json: {
        generatedAt: "2026-08-02T04:00:00Z",
        freshnessSeconds: 120,
        targets: [{
          velocityTarget: "activity",
          servers: [{ serverId: "activity", displayName: "活动服", isVisible: true }],
          hasHeartbeat: true,
          isFresh: true,
          online: true,
          onlinePlayers: 8,
          maxPlayers: 30,
          softwareVersion: "NeoForge 21.11.42 / Minecraft 1.21.11",
          protocolVersion: 774,
          processWorkingSetBytes: 4294967296,
          processPrivateBytes: 5368709120,
          processCpuPercent: 31.5,
          processStartedAt: "2026-08-02T02:00:00Z",
          diskFreeBytes: 107374182400,
          diskTotalBytes: 214748364800,
          tps1m: 20,
          tps5m: 19.9,
          tps15m: 19.8,
          msptAverage: 18.4,
          gcCollectionTimeMilliseconds: 92,
          metricsCapturedAt: now,
          issues: [],
          collectorInstance: "owl5",
          capturedAt: now,
          receivedAt: now
        }],
        issues: []
      } });
    } else if (path === "/v1/admin/operational-alerts") {
      await route.fulfill({ json: {
        generatedAt: "2026-08-02T04:00:00Z",
        activeCount: 1,
        criticalCount: 0,
        warningCount: 1,
        unacknowledgedCount: 1,
        alerts: [{
          fingerprint: "server:activity:disk",
          code: "ServerDiskLow",
          source: "Server",
          severity: "Warning",
          status: "Active",
          title: "活动服磁盘余量偏低",
          summary: "可用空间低于预警线。",
          openedAt: "2026-08-02T03:00:00Z",
          lastSeenAt: now,
          lastTransitionAt: "2026-08-02T03:00:00Z",
          resolvedAt: null,
          observationCount: 4,
          acknowledgedAt: null,
          acknowledgedBy: null,
          revision: 1
        }]
      } });
    } else if (path === "/v1/admin/diagnostics") {
      await route.fulfill({ json: [{
        uploadId: "abcd1234",
        userId: userSummary.userId,
        accountDisplayName: userSummary.displayName,
        profileId: profileSummary.id,
        launcherVersion: "0.14.2",
        size: 2048,
        sha256: hashOne,
        uploadedAt: now,
        expiresAt: "2026-08-16T04:00:00Z"
      }] });
    } else if (path === "/v1/admin/audit-logs") {
      await route.fulfill({ json: [{
        id: 128,
        actorUserId: session.player.userId,
        actorDisplayName: session.player.minecraftName,
        action: "catalog.server.updated",
        targetType: "server",
        targetId: "activity",
        sourceIp: "127.0.0.1",
        beforeData: { maxPlayers: 20 },
        afterData: { maxPlayers: 30 },
        createdAt: now
      }] });
    } else {
      await route.fulfill({ status: 404, json: { detail: `Unhandled test endpoint: ${request.method()} ${path}` } });
    }
  });
}

async function dragCalendarHandleByDays(
  page: Page,
  handle: Locator,
  referenceDay: Locator,
  days: number
): Promise<void> {
  const [handleBox, dayBox] = await Promise.all([
    handle.boundingBox(),
    referenceDay.boundingBox()
  ]);
  expect(handleBox).not.toBeNull();
  expect(dayBox).not.toBeNull();
  const startX = handleBox!.x + handleBox!.width / 2;
  const startY = handleBox!.y + handleBox!.height / 2;
  await page.mouse.move(startX, startY);
  await page.mouse.down();
  await page.mouse.move(startX + dayBox!.width * days, startY, { steps: 12 });
  await page.mouse.up();
}

test("control polling preserves dirty settings and console reading position", async ({ page }) => {
  await mockAdminApi(page);
  await page.goto("/admin/control");
  await expect(page.locator(".page-heading h1")).toHaveText("服控面板");
  await expect(page.getByLabel("最大玩家数")).toHaveValue("30");

  await page.getByLabel("最大玩家数").fill("42");
  await expect(page.getByText("有未保存更改")).toBeVisible();

  const output = page.locator(".control-console-output");
  await page.getByLabel("跟随末尾").uncheck();
  await output.evaluate(element => { element.scrollTop = 60; });
  const before = await output.evaluate(element => element.scrollTop);
  await page.waitForTimeout(3_300);

  await expect(page.getByLabel("最大玩家数")).toHaveValue("42");
  const after = await output.evaluate(element => element.scrollTop);
  expect(Math.abs(after - before)).toBeLessThanOrEqual(2);
  await page.screenshot({ path: "../../../artifacts/admin-web-control-desktop.png", fullPage: true });
});

test("control deep link selects the requested non-default target", async ({ page }) => {
  await mockAdminApi(page, {
    intercept: async (route, request, path) => {
      if (path !== "/v1/admin/server-control/targets/fanstreet" || request.method() !== "GET") return false;
      const result = controlDetail(1);
      result.target = {
        ...result.target,
        ...controlOverview(1).targets[1],
        allowedCommandPrefixes: ["list", "save-all", "whitelist"],
        consoleTail: "[04:00:00 INFO] fanstreet ready"
      };
      await route.fulfill({ json: result });
      return true;
    }
  });

  await page.goto("/admin/control?server=fanstreet");

  await expect(page.locator(".control-detail-heading h3")).toHaveText("范街活动服");
  await expect(page).toHaveURL(/\/admin\/control\?server=fanstreet$/);
  await expect(page.locator(".control-target-item.active")).toContainText("范街活动服");
});

test("invalid control deep link falls back with an explicit warning", async ({ page }) => {
  await mockAdminApi(page);

  await page.goto("/admin/control?server=missing-target");

  await expect(page.getByRole("alert")).toContainText("未找到服控目标 missing-target，已切换到 活动服。");
  await expect(page).toHaveURL(/\/admin\/control\?server=activity$/);
  await expect(page.locator(".control-detail-heading h3")).toHaveText("活动服");
});

test("server file deletion requires stopped state and exact destructive confirmation", async ({ page }) => {
  let deleteBody: Record<string, unknown> | null = null;
  await mockAdminApi(page, {
    intercept: async (route, request, path) => {
      if (path === "/v1/admin/server-control/overview") {
        const stopped = controlOverview(1);
        stopped.targets[0] = {
          ...stopped.targets[0],
          online: false,
          processId: null
        };
        await route.fulfill({ json: stopped });
        return true;
      }
      if (path === "/v1/admin/server-control/targets/activity" && request.method() === "GET") {
        const stopped = controlDetail(1);
        stopped.target = {
          ...stopped.target,
          online: false,
          processId: null
        };
        await route.fulfill({ json: stopped });
        return true;
      }
      if (path === "/v1/admin/server-control/targets/activity/operations" && request.method() === "POST") {
        deleteBody = request.postDataJSON() as Record<string, unknown>;
        await route.fulfill({
          status: 202,
          json: {
            operation: {
              operationId: "99999999-9999-9999-9999-999999999999",
              serverId: "activity",
              displayName: "活动服",
              action: "DeleteServerFiles",
              status: "Pending",
              reason: deleteBody.reason,
              requestedBy: session.player.userId,
              requestedAt: now,
              startedAt: null,
              completedAt: null,
              resultCode: null,
              resultMessage: null,
              automaticallyStoppingServerIds: []
            },
            automaticallyStoppingServerIds: []
          }
        });
        return true;
      }
      return false;
    }
  });

  await page.goto("/admin/control");
  await page.getByRole("button", { name: "删除服务端文件", exact: true }).click();
  await expect(page.getByRole("heading", { name: "永久删除服务端文件" })).toBeVisible();
  await expect(page.getByText("此操作不可恢复。", { exact: false })).toBeVisible();
  await page.getByLabel("操作原因").fill("活动录制结束，释放 VPS 磁盘空间");
  await page.getByLabel("二次确认").fill("DELETE activity");
  await page.getByRole("button", { name: "确认删除服务端文件" }).click();

  await expect.poll(() => deleteBody).not.toBeNull();
  expect(deleteBody).toMatchObject({
    action: "DeleteServerFiles",
    confirmation: "DELETE activity",
    reason: "活动录制结束，释放 VPS 磁盘空间",
    consoleCommand: null,
    settings: null
  });
  await page.screenshot({
    path: "../../../artifacts/admin-web-control-delete-server.png",
    fullPage: true
  });
});

test("completed server deletion leaves the active list and returns after redeployment", async ({ page }) => {
  let deleted = false;
  let redeployed = false;
  await mockAdminApi(page, {
    intercept: async (route, request, path) => {
      if (path === "/v1/admin/server-control/overview") {
        const result = controlOverview(1);
        result.targets[0] = {
          ...result.targets[0],
          online: false,
          processId: null,
          serverFilesPresent: !deleted || redeployed,
          deletionCleanupPending: false
        };
        await route.fulfill({ json: result });
        return true;
      }
      if (path === "/v1/admin/server-control/targets/activity" && request.method() === "GET") {
        const result = controlDetail(1);
        result.target = {
          ...result.target,
          online: false,
          processId: null,
          serverFilesPresent: !deleted || redeployed,
          deletionCleanupPending: false
        };
        await route.fulfill({ json: result });
        return true;
      }
      if (path === "/v1/admin/server-control/targets/fanstreet" && request.method() === "GET") {
        const result = controlDetail(1);
        result.target = {
          ...result.target,
          ...controlOverview(1).targets[1],
          allowedCommandPrefixes: ["list", "save-all", "whitelist"],
          consoleTail: ""
        };
        await route.fulfill({ json: result });
        return true;
      }
      return false;
    }
  });

  await page.goto("/admin/control");
  const targetList = page.locator(".control-target-list");
  await expect(targetList.getByText("活动服", { exact: true })).toBeVisible();
  await expect(page.locator(".control-pane-heading")).toContainText("2 个目标");

  deleted = true;
  await page.getByRole("button", { name: "刷新" }).click();
  await expect(targetList.getByText("活动服", { exact: true })).toHaveCount(0);
  await expect(page.locator(".control-pane-heading")).toContainText("1 个目标");
  await expect(page.locator(".control-detail-heading h3")).toHaveText("范街活动服");
  await expect(page.getByRole("button", { name: "删除服务端文件", exact: true })).toBeVisible();

  redeployed = true;
  await page.getByRole("button", { name: "刷新" }).click();
  await expect(targetList.getByText("活动服", { exact: true })).toBeVisible();
  await expect(page.locator(".control-pane-heading")).toContainText("2 个目标");
});

test("every production channel change requires confirmation", async ({ page }) => {
  let productionBody: Record<string, unknown> | null = null;
  await mockAdminApi(page, { onProductionUpdate: body => { productionBody = body; } });
  await page.goto("/admin/profiles");
  await expect(page.locator(".page-heading h1")).toHaveText("客户端档案");
  await page.getByRole("button", { name: "管理客户端档案" }).click();
  await expect(page.locator(".profile-drawer")).toBeVisible();

  const productionCard = page.locator(".profile-channel-card").filter({ hasText: "正式通道" });
  await productionCard.getByLabel("发布版本").selectOption(hashTwo);
  await productionCard.getByRole("button", { name: "保存通道" }).click();
  await expect(page.getByRole("heading", { name: "切换正式版本" })).toBeVisible();
  await page.getByRole("button", { name: "确认设为正式" }).click();

  await expect.poll(() => productionBody).not.toBeNull();
  expect(productionBody).toMatchObject({
    manifestSha256: hashTwo,
    rolloutPercentage: 100,
    expectedRevision: 3
  });
  await page.screenshot({ path: "../../../artifacts/admin-web-profiles-desktop.png", fullPage: true });
});

test("client profile lifecycle archives, restores, and permanently deletes only an empty draft", async ({ page }) => {
  const profileId = "unused-draft-1.21.11";
  let exists = true;
  let current = {
    ...profileSummary,
    id: profileId,
    displayName: "误建的空档案",
    version: "unpublished",
    downloadBytes: 0,
    sha256: "",
    isActive: false,
    isArchived: false,
    archivedAt: null as string | null,
    archiveReason: "",
    serverReferenceCount: 0,
    canDelete: false,
    revision: 1,
    releaseCount: 0,
    channels: profileSummary.channels.map(channel => ({
      ...channel,
      manifestSha256: null,
      version: null,
      revision: 1
    }))
  };
  const archiveBodies: Record<string, unknown>[] = [];
  let restoreBody: Record<string, unknown> | null = null;
  let deleteBody: Record<string, unknown> | null = null;

  await mockAdminApi(page, {
    intercept: async (route, request, path) => {
      if (path === "/v1/admin/catalog/client-profiles" && request.method() === "GET") {
        await route.fulfill({ json: exists ? [current] : [] });
        return true;
      }
      if (path === `/v1/admin/catalog/client-profiles/${profileId}` && request.method() === "GET") {
        await route.fulfill({ json: { profile: current, releases: [] } });
        return true;
      }
      if (path === `/v1/admin/catalog/client-profiles/${profileId}/archive` && request.method() === "POST") {
        const body = request.postDataJSON() as Record<string, unknown>;
        archiveBodies.push(body);
        current = {
          ...current,
          isActive: false,
          isArchived: true,
          archivedAt: now,
          archiveReason: String(body.reason),
          canDelete: true,
          revision: current.revision + 1
        };
        await route.fulfill({ json: { profile: current, releases: [] } });
        return true;
      }
      if (path === `/v1/admin/catalog/client-profiles/${profileId}/restore` && request.method() === "POST") {
        restoreBody = request.postDataJSON() as Record<string, unknown>;
        current = {
          ...current,
          isArchived: false,
          archivedAt: null,
          archiveReason: "",
          canDelete: false,
          revision: current.revision + 1
        };
        await route.fulfill({ json: { profile: current, releases: [] } });
        return true;
      }
      if (path === `/v1/admin/catalog/client-profiles/${profileId}` && request.method() === "DELETE") {
        deleteBody = request.postDataJSON() as Record<string, unknown>;
        exists = false;
        await route.fulfill({ status: 204 });
        return true;
      }
      return false;
    }
  });

  await page.goto("/admin/profiles");
  await page.getByRole("button", { name: "管理客户端档案" }).click();
  const drawer = page.locator(".profile-drawer");
  await drawer.getByRole("button", { name: "归档档案" }).click();
  await page.getByLabel("归档原因").fill("误建测试档案，先归档确认");
  await page.getByRole("button", { name: "确认归档" }).click();
  await expect.poll(() => archiveBodies.length).toBe(1);
  expect(archiveBodies[0]).toMatchObject({
    reason: "误建测试档案，先归档确认",
    expectedRevision: 1
  });
  await drawer.locator("#profile-lifecycle-title").scrollIntoViewIfNeeded();
  await expect(drawer.getByRole("button", { name: "永久删除" })).toBeEnabled();
  await page.screenshot({
    path: "../../../artifacts/admin-web-profile-lifecycle-drawer-desktop.png"
  });

  await drawer.getByRole("button", { name: "完成" }).click();
  await expect(page.getByText("没有使用中的档案")).toBeVisible();
  await page.getByRole("button", { name: "已归档" }).click();
  await expect(page.getByText("误建的空档案")).toBeVisible();
  await page.getByRole("button", { name: "管理客户端档案" }).click();
  await drawer.getByRole("button", { name: "恢复档案" }).click();
  await page.getByRole("button", { name: "确认恢复" }).click();
  await expect.poll(() => restoreBody).not.toBeNull();
  expect(restoreBody).toMatchObject({ expectedRevision: 2 });

  await drawer.getByRole("button", { name: "完成" }).click();
  await expect(page.getByText("没有已归档档案")).toBeVisible();
  await page.getByRole("button", { name: "使用中" }).click();
  await page.getByRole("button", { name: "管理客户端档案" }).click();
  await drawer.getByRole("button", { name: "归档档案" }).click();
  await page.getByLabel("归档原因").fill("确认空档案可以安全清理");
  await page.getByRole("button", { name: "确认归档" }).click();
  await expect.poll(() => archiveBodies.length).toBe(2);

  await page.setViewportSize({ width: 390, height: 844 });
  await drawer.getByRole("button", { name: "永久删除" }).scrollIntoViewIfNeeded();
  const drawerWidth = await drawer.evaluate(element => ({
    scrollWidth: element.scrollWidth,
    clientWidth: element.clientWidth
  }));
  expect(drawerWidth.scrollWidth).toBe(drawerWidth.clientWidth);
  await expect(drawer.getByRole("button", { name: "恢复档案" })).toBeVisible();
  await expect(drawer.getByRole("button", { name: "永久删除" })).toBeVisible();
  await page.screenshot({
    path: "../../../artifacts/admin-web-profile-lifecycle-drawer-mobile.png"
  });

  await drawer.getByRole("button", { name: "永久删除" }).click();
  await page.getByLabel("删除原因").fill("清理误建且从未发布的空档案");
  await page.getByLabel("二次确认").fill(`DELETE ${profileId}`);
  await page.getByRole("button", { name: "确认永久删除" }).click();
  await expect.poll(() => deleteBody).not.toBeNull();
  expect(deleteBody).toMatchObject({
    reason: "清理误建且从未发布的空档案",
    confirmation: `DELETE ${profileId}`,
    expectedRevision: 4
  });
  await expect(page.getByText("没有使用中的档案")).toBeVisible();
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.screenshot({
    path: "../../../artifacts/admin-web-profile-lifecycle-desktop.png",
    fullPage: true
  });
});

test("archived profiles with immutable releases explain why permanent deletion is blocked", async ({ page }) => {
  const archived = {
    ...profileSummary,
    isActive: false,
    isArchived: true,
    archivedAt: now,
    archiveReason: "历史测试版本停止使用",
    serverReferenceCount: 0,
    canDelete: false
  };
  await mockAdminApi(page, {
    intercept: async (route, request, path) => {
      if (path === "/v1/admin/catalog/client-profiles" && request.method() === "GET") {
        await route.fulfill({ json: [archived] });
        return true;
      }
      if (path === `/v1/admin/catalog/client-profiles/${archived.id}` && request.method() === "GET") {
        await route.fulfill({ json: { profile: archived, releases } });
        return true;
      }
      return false;
    }
  });

  await page.goto("/admin/profiles");
  await expect(page.getByText("没有使用中的档案")).toBeVisible();
  await page.getByRole("button", { name: "已归档" }).click();
  await page.getByRole("button", { name: "管理客户端档案" }).click();
  const drawer = page.locator(".profile-drawer");
  await drawer.locator("#profile-lifecycle-title").scrollIntoViewIfNeeded();
  await expect(drawer.getByRole("button", { name: "永久删除" })).toBeDisabled();
  await expect(drawer.getByText(/保留 2 个不可变版本/)).toBeVisible();
});

test("package import defaults to publishing artifacts without deploying the activity slot", async ({ page }) => {
  const uploadBytes = 1024 * 1024;
  let uploadOffset = 0;
  let detailReads = 0;
  let confirmedBody: Record<string, unknown> | null = null;
  let record: PackageImportMock = {
    ...completedPackageImport,
    uploadedBytes: 0,
    sourceSha256: null,
    status: "Uploading",
    analysis: null,
    plan: null,
    manifestSha256: null,
    deploymentOperationId: null,
    completedAt: null,
    revision: 1
  };
  await mockAdminApi(page, {
    intercept: async (route, request, path) => {
      if (path === "/v1/admin/server-control/overview") {
        expect(new URL(request.url()).searchParams.get("includeDeletedTargets")).toBe("true");
        const stopped = controlOverview(1);
        stopped.targets[0] = {
          ...stopped.targets[0],
          online: false,
          processId: null,
          settings: null,
          serverFilesPresent: false,
          packageDeploymentMemoryGuidance: {
            hostTotalMemoryMiB: 32768,
            recommendedMinimumMemoryMiB: 4096,
            recommendedMaximumMemoryMiB: 16384
          }
        };
        await route.fulfill({ json: stopped });
        return true;
      }
      if (path === "/v1/admin/package-imports" && request.method() === "GET") {
        await route.fulfill({ json: {
          imports: [record],
          publisherAgentConnected: true,
          publisherAgentLastSeenAt: now
        } });
        return true;
      }
      if (path === "/v1/admin/package-imports/uploads" && request.method() === "POST") {
        await route.fulfill({ status: 201, json: record });
        return true;
      }
      if (path === `/v1/admin/package-imports/${packageImportId}` && request.method() === "GET") {
        detailReads += 1;
        await route.fulfill({ json: record });
        return true;
      }
      if (path === `/v1/admin/package-imports/${packageImportId}/content` && request.method() === "PATCH") {
        expect(request.headers()["upload-offset"]).toBe(String(uploadOffset));
        uploadOffset += request.postDataBuffer()?.byteLength ?? 0;
        record = { ...record, uploadedBytes: uploadOffset };
        await route.fulfill({ json: {
          importId: packageImportId,
          uploadedBytes: uploadOffset,
          expectedUploadBytes: uploadBytes,
          complete: uploadOffset === uploadBytes
        } });
        return true;
      }
      if (path === `/v1/admin/package-imports/${packageImportId}/complete` && request.method() === "POST") {
        record = {
          ...record,
          uploadedBytes: uploadBytes,
          sourceSha256: hashOne,
          status: "AwaitingReview",
          analysis: packageAnalysis,
          revision: 4
        };
        await route.fulfill({ json: record });
        return true;
      }
      if (path === `/v1/admin/package-imports/${packageImportId}/confirm` && request.method() === "POST") {
        confirmedBody = request.postDataJSON() as Record<string, unknown>;
        record = {
          ...record,
          status: "QueuedForPublishing",
          plan: completedPackageImport.plan,
          revision: 5
        };
        await route.fulfill({ json: record });
        return true;
      }
      return false;
    }
  });

  await page.goto("/admin/package-imports");
  await page.locator('input[type="file"]').first().setInputFiles({
    name: "summer.zip",
    mimeType: "application/zip",
    buffer: Buffer.alloc(uploadBytes, 7)
  });
  await page.getByRole("button", { name: "上传并识别" }).click();

  const importDrawer = page.locator(".package-import-drawer");
  await expect(importDrawer).toBeVisible();
  await expect(importDrawer.getByRole("heading", { name: "等待确认" })).toBeVisible();
  await expect(importDrawer.getByLabel("仅发布并入库")).toBeChecked();
  await expect(importDrawer.getByText("目标已停服", { exact: true })).toHaveCount(0);
  await expect(importDrawer.getByText("VPS 总内存")).toBeVisible();
  await expect(importDrawer.getByText("32 GiB")).toBeVisible();
  await importDrawer.getByLabel("最大内存（GiB）").fill("20");
  await expect(importDrawer.getByText("高于推荐区间，仍可提交")).toBeVisible();
  const confirmationInput = importDrawer.getByLabel("精确确认");
  const confirmation = `发布并入库 ${packageImportId}`;
  await confirmationInput.fill(confirmation);
  const readsAfterTyping = detailReads;
  await expect.poll(() => detailReads, { timeout: 7_000 }).toBeGreaterThan(readsAfterTyping);
  await expect(confirmationInput).toHaveValue(confirmation);
  await expect(importDrawer.getByText("有未提交更改")).toBeVisible();
  await expect(importDrawer.getByRole("button", { name: "发布并入库" })).toBeEnabled();
  await importDrawer.getByRole("button", { name: "发布并入库" }).click();

  await expect.poll(() => confirmedBody).not.toBeNull();
  expect(uploadOffset).toBe(uploadBytes);
  expect(confirmedBody).toMatchObject({
    expectedRevision: 4,
    profileId: "summer-neoforge-1.21.11",
    targetServerId: "activity",
    preserveWorldData: false,
    syncServerCatalog: true,
    maximumMemoryMiB: 20480,
    deployServer: false,
    confirmation: `发布并入库 ${packageImportId}`
  });
  await page.screenshot({ path: "../../../artifacts/admin-web-package-import-desktop.png", fullPage: true });
});

test("package import deploys immediately only after the administrator selects compatibility mode", async ({ page }) => {
  let confirmedBody: Record<string, unknown> | null = null;
  const reviewRecord: PackageImportMock = {
    ...completedPackageImport,
    status: "AwaitingReview",
    plan: null,
    manifestSha256: null,
    deploymentOperationId: null,
    completedAt: null,
    revision: 4
  };
  await mockAdminApi(page, {
    intercept: async (route, request, path) => {
      if (path === "/v1/admin/server-control/overview") {
        const stopped = controlOverview(1);
        stopped.targets[0] = { ...stopped.targets[0], online: false, processId: null };
        await route.fulfill({ json: stopped });
        return true;
      }
      if (path === "/v1/admin/package-imports" && request.method() === "GET") {
        await route.fulfill({ json: {
          imports: [reviewRecord],
          publisherAgentConnected: true,
          publisherAgentLastSeenAt: now
        } });
        return true;
      }
      if (path === `/v1/admin/package-imports/${packageImportId}` && request.method() === "GET") {
        await route.fulfill({ json: reviewRecord });
        return true;
      }
      if (path === `/v1/admin/package-imports/${packageImportId}/confirm` && request.method() === "POST") {
        confirmedBody = request.postDataJSON() as Record<string, unknown>;
        await route.fulfill({ json: {
          ...reviewRecord,
          status: "QueuedForPublishing",
          plan: { ...completedPackageImport.plan, deployServer: true },
          revision: 5
        } });
        return true;
      }
      return false;
    }
  });

  await page.goto("/admin/package-imports");
  await page.getByRole("button", { name: "查看整合包任务" }).click();
  const drawer = page.locator(".package-import-drawer");
  await drawer.getByLabel("立即部署活动槽").check();
  await expect(drawer.getByText("服控代理", { exact: true })).toHaveClass(/ready/);
  await expect(drawer.getByText("目标已停服", { exact: true })).toHaveClass(/ready/);
  await drawer.getByLabel("精确确认").fill(`发布并部署 ${packageImportId}`);
  await drawer.getByRole("button", { name: "发布并部署" }).click();

  await expect.poll(() => confirmedBody).not.toBeNull();
  expect(confirmedBody).toMatchObject({
    expectedRevision: 4,
    deployServer: true,
    confirmation: `发布并部署 ${packageImportId}`
  });
});

test("completed package deployment hands off to the exact server control target", async ({ page }) => {
  const deployedRecord: PackageImportMock = {
    ...completedPackageImport,
    plan: { ...completedPackageImport.plan, deployServer: true }
  };
  await mockAdminApi(page, {
    intercept: async (route, request, path) => {
      if (path === "/v1/admin/server-control/overview") {
        const stopped = controlOverview(1);
        stopped.targets[0] = { ...stopped.targets[0], online: false, processId: null };
        await route.fulfill({ json: stopped });
        return true;
      }
      if (path === "/v1/admin/package-imports" && request.method() === "GET") {
        await route.fulfill({ json: {
          imports: [deployedRecord],
          publisherAgentConnected: true,
          publisherAgentLastSeenAt: now
        } });
        return true;
      }
      if (path === `/v1/admin/package-imports/${packageImportId}` && request.method() === "GET") {
        await route.fulfill({ json: deployedRecord });
        return true;
      }
      return false;
    }
  });

  await page.goto("/admin/package-imports");
  await page.getByRole("button", { name: "查看整合包任务" }).click();
  const drawer = page.locator(".package-import-drawer");
  await expect(drawer.getByRole("heading", { name: "服务端文件已部署，等待首次启动验收" })).toBeVisible();
  await expect(drawer.getByText("部署完成只代表文件切换成功。", { exact: false })).toBeVisible();
  await page.setViewportSize({ width: 390, height: 844 });
  const drawerWidth = await drawer.evaluate(element => ({
    scrollWidth: element.scrollWidth,
    clientWidth: element.clientWidth
  }));
  expect(drawerWidth.scrollWidth).toBe(drawerWidth.clientWidth);
  await expect(drawer.getByRole("button", { name: "启动服务端" })).toBeVisible();
  await drawer.getByRole("button", { name: "启动服务端" }).click();

  await expect(page).toHaveURL(/\/admin\/control\?server=activity$/);
  await expect(page.locator(".control-detail-heading h3")).toHaveText("活动服");
});

test("package publishing shows real progress and estimates remaining time", async ({ page }) => {
  let detailReads = 0;
  const publishingRecord = {
    ...completedPackageImport,
    status: "PublishingClient",
    completedAt: null,
    revision: 9,
    publisherProgress: {
      phase: "WaitingForWorkingSpace",
      completedObjects: 0,
      totalObjects: 0,
      processedBytes: 3_650_000_000,
      totalBytes: 7_311_430_775,
      sampledAt: "2026-08-06T04:00:00Z"
    }
  };
  await mockAdminApi(page, {
    intercept: async (route, request, path) => {
      if (path === "/v1/admin/package-imports" && request.method() === "GET") {
        await route.fulfill({ json: {
          imports: [publishingRecord],
          publisherAgentConnected: true,
          publisherAgentLastSeenAt: now
        } });
        return true;
      }
      if (path === `/v1/admin/package-imports/${packageImportId}` && request.method() === "GET") {
        detailReads += 1;
        await route.fulfill({ json: {
          ...publishingRecord,
          publisherProgress: detailReads === 1
            ? publishingRecord.publisherProgress
            : {
                phase: "PublishingObjects",
                completedObjects: detailReads === 2 ? 20 : 40,
                totalObjects: 100,
                processedBytes: detailReads === 2 ? 2_097_152 : 4_194_304,
                totalBytes: 10_485_760,
                sampledAt: detailReads === 2
                  ? "2026-08-06T04:00:00Z"
                  : "2026-08-06T04:00:03Z"
              }
        } });
        return true;
      }
      return false;
    }
  });

  await page.goto("/admin/package-imports");
  await page.getByRole("button", { name: "查看整合包任务" }).click();
  const drawer = page.locator(".package-import-drawer");
  const progressbar = drawer.getByRole("progressbar", { name: "客户端发布进度" });
  await expect(progressbar).not.toHaveAttribute("aria-valuenow", /.+/);
  await expect(drawer.getByText("等待 Publisher 工作空间")).toBeVisible();
  await expect(drawer.getByText(/可用 3\.4 GiB · 需要 6\.8 GiB/)).toBeVisible();
  await expect(progressbar).toHaveAttribute("aria-valuenow", "20", { timeout: 7_000 });
  await expect(drawer.getByText("正在计算剩余时间")).toBeVisible();
  await expect(progressbar).toHaveAttribute("aria-valuenow", "40", { timeout: 7_000 });
  await expect(drawer.getByText("40% · 预计剩余 9 秒")).toBeVisible();
  await expect(drawer.getByText("40 / 100 个对象 · 4.0 MiB / 10 MiB")).toBeVisible();
  await page.screenshot({ path: "../../../artifacts/admin-web-package-progress-desktop.png", fullPage: true });

  await page.setViewportSize({ width: 390, height: 844 });
  await expect(progressbar).toBeVisible();
  const progressBox = await progressbar.boundingBox();
  expect(progressBox && progressBox.x >= 0 && progressBox.x + progressBox.width <= 390).toBe(true);
  await page.screenshot({ path: "../../../artifacts/admin-web-package-progress-mobile.png", fullPage: true });
});

test("production CSP keeps the activity calendar route loadable", async ({ page }) => {
  const pageErrors: Error[] = [];
  page.on("pageerror", error => pageErrors.push(error));
  await page.route("**/admin/package-imports", async route => {
    const response = await route.fetch();
    await route.fulfill({
      response,
      headers: {
        ...response.headers(),
        "content-security-policy":
          "default-src 'self'; script-src 'self'; " +
          "style-src 'self' 'sha256-ipzKv5H4ieKlTTlJ/yUoqe2zh7iU5Iy8a9PrIETK5us='; " +
          "img-src 'self' data:; connect-src 'self'; font-src 'self'; object-src 'none'; " +
          "base-uri 'none'; form-action 'self'; frame-ancestors 'none'"
      }
    });
  });
  await mockAdminApi(page);

  await page.goto("/admin/package-imports");
  await page.getByRole("link", { name: "活动企划" }).click();

  await expect(page).toHaveURL(/\/admin\/activity-plans$/);
  await expect(page.locator(".page-heading h1")).toHaveText("活动企划");
  await expect(page.locator(".activity-calendar-panel .fc")).toBeVisible();
  expect(pageErrors.some(error => error.message.includes("cssRules"))).toBe(false);
});

test("activity calendar creates a draft from a selected date with a bound package", async ({ page }) => {
  let createBody: Record<string, unknown> | null = null;
  await page.clock.setFixedTime(new Date("2026-08-10T08:00:00+08:00"));
  await mockAdminApi(page, {
    intercept: async (route, request, path) => {
      if (path === "/v1/admin/activity-plans" && request.method() === "POST") {
        createBody = request.postDataJSON() as Record<string, unknown>;
        await route.fulfill({ status: 201, json: {
          ...activityPlanOverview.plans[1],
          ...createBody,
          id: "activity-plan-20260818-c1d2e3f4",
          status: "Draft",
          effectiveStatus: "Closed",
          productionReady: true,
          deploymentMatches: true,
          revision: 1,
          createdAt: now,
          updatedAt: now,
          profileId: "summer-neoforge-1.21.11",
          profileDisplayName: "夏日活动",
          version: "1.0.0",
          minecraftVersion: "1.21.11",
          loader: "NeoForge"
        } });
        return true;
      }
      return false;
    }
  });

  await page.goto("/admin/activity-plans");
  await expect(page.locator(".page-heading h1")).toHaveText("活动企划");
  await expect(page.getByText("同一时间只开放一个活动")).toBeVisible();
  await page.locator('.fc-daygrid-day[data-date="2026-08-18"]').click();
  await expect(page.getByRole("heading", { name: "创建活动企划" })).toBeVisible();
  await expect(page.getByLabel("开放时间")).toHaveValue("2026-08-18T19:00");
  await expect(page.getByLabel("结束时间")).toHaveValue("2026-08-18T22:00");
  await page.getByLabel("企划名称").fill("夏日红石接力");
  await page.getByRole("button", { name: "创建草稿" }).click();

  await expect.poll(() => createBody).not.toBeNull();
  expect(createBody).toMatchObject({
    title: "夏日红石接力",
    packageImportId,
    maximumPlayers: 30,
    minimumTier: "Participant"
  });
  expect(new Date(String(createBody!.opensAt)).toISOString()).toBe("2026-08-18T11:00:00.000Z");
  expect(new Date(String(createBody!.closesAt)).toISOString()).toBe("2026-08-18T14:00:00.000Z");
  await page.screenshot({ path: "../../../artifacts/admin-web-activity-plans-desktop.png", fullPage: true });
});

test("activity calendar moves and resizes both boundaries while preserving wall-clock time", async ({ page }) => {
  const updates: Record<string, unknown>[] = [];
  let currentPlan = { ...activityPlanOverview.plans[0] };
  await page.clock.setFixedTime(new Date("2026-08-10T08:00:00+08:00"));
  await mockAdminApi(page, {
    intercept: async (route, request, path) => {
      if (path === "/v1/admin/activity-plans" && request.method() === "GET") {
        await route.fulfill({
          json: {
            ...activityPlanOverview,
            plans: [currentPlan, activityPlanOverview.plans[1]]
          }
        });
        return true;
      }
      if (
        path === `/v1/admin/activity-plans/${currentPlan.id}` &&
        request.method() === "PUT"
      ) {
        const body = request.postDataJSON() as Record<string, unknown>;
        updates.push(body);
        currentPlan = {
          ...currentPlan,
          opensAt: String(body.opensAt),
          closesAt: String(body.closesAt),
          revision: currentPlan.revision + 1,
          updatedAt: now
        };
        await route.fulfill({ json: currentPlan });
        return true;
      }
      return false;
    }
  });

  await page.goto("/admin/activity-plans");
  const event = page.locator(".fc-event").filter({ hasText: "夏日建筑接力" }).first();
  await expect(event).toBeVisible();
  await event.hover();
  const startHandle = event.locator(".fc-event-resizer-start");
  const endHandle = event.locator(".fc-event-resizer-end");
  await expect(startHandle).toHaveCount(1);
  await expect(endHandle).toHaveCount(1);
  await expect(startHandle).toBeVisible();
  await expect(endHandle).toBeVisible();

  await dragCalendarHandleByDays(
    page,
    startHandle,
    page.locator('.fc-daygrid-day[data-date="2026-08-12"]'),
    -1
  );
  await expect.poll(() => updates.length).toBe(1);
  expect(new Date(String(updates[0].opensAt)).toISOString()).toBe("2026-08-11T11:00:00.000Z");
  expect(new Date(String(updates[0].closesAt)).toISOString()).toBe("2026-08-12T14:00:00.000Z");

  await expect(event).toBeVisible();
  await event.hover();
  await dragCalendarHandleByDays(
    page,
    event.locator(".fc-event-resizer-end"),
    page.locator('.fc-daygrid-day[data-date="2026-08-12"]'),
    1
  );
  await expect.poll(() => updates.length).toBe(2);
  expect(new Date(String(updates[1].opensAt)).toISOString()).toBe("2026-08-11T11:00:00.000Z");
  expect(new Date(String(updates[1].closesAt)).toISOString()).toBe("2026-08-13T14:00:00.000Z");

  await expect(event).toBeVisible();
  await dragCalendarHandleByDays(
    page,
    event,
    page.locator('.fc-daygrid-day[data-date="2026-08-12"]'),
    1
  );
  await expect.poll(() => updates.length).toBe(3);
  expect(new Date(String(updates[2].opensAt)).toISOString()).toBe("2026-08-12T11:00:00.000Z");
  expect(new Date(String(updates[2].closesAt)).toISOString()).toBe("2026-08-14T14:00:00.000Z");
});

test("activity plans can publish back-to-back without overlapping", async ({ page }) => {
  let publishCalls = 0;
  const boundaryDraft = {
    ...activityPlanOverview.plans[1],
    opensAt: activityPlanOverview.plans[0].closesAt,
    closesAt: "2026-08-12T17:00:00Z"
  };
  await page.clock.setFixedTime(new Date("2026-08-10T08:00:00+08:00"));
  await mockAdminApi(page, {
    intercept: async (route, request, path) => {
      if (path === "/v1/admin/activity-plans" && request.method() === "GET") {
        await route.fulfill({
          json: {
            ...activityPlanOverview,
            plans: [activityPlanOverview.plans[0], boundaryDraft]
          }
        });
        return true;
      }
      if (
        path === `/v1/admin/activity-plans/${boundaryDraft.id}/publish` &&
        request.method() === "POST"
      ) {
        publishCalls += 1;
        await route.fulfill({
          json: {
            ...boundaryDraft,
            status: "Published",
            revision: boundaryDraft.revision + 1,
            updatedAt: now
          }
        });
        return true;
      }
      return false;
    }
  });

  await page.goto("/admin/activity-plans");
  await page.locator(".fc-event").filter({ hasText: "周末生存挑战" }).click();
  const [calendarGridBox, inspectorBox] = await Promise.all([
    page.locator(".activity-calendar-panel .fc-scrollgrid").boundingBox(),
    page.locator(".activity-plan-inspector").boundingBox()
  ]);
  expect(calendarGridBox).not.toBeNull();
  expect(inspectorBox).not.toBeNull();
  expect(calendarGridBox!.x + calendarGridBox!.width).toBeLessThanOrEqual(inspectorBox!.x + 1);
  const publishButton = page
    .locator(".activity-plan-inspector")
    .getByRole("button", { name: "发布企划" });
  await expect(publishButton).toBeEnabled();
  await publishButton.click();
  await page.getByRole("button", { name: "确认发布" }).click();
  await expect.poll(() => publishCalls).toBe(1);
});

test("draft activity can overlap for planning but cannot be published", async ({ page }) => {
  const conflictingOverview = {
    ...activityPlanOverview,
    plans: activityPlanOverview.plans.map((plan, index) => index === 1 ? {
      ...plan,
      opensAt: "2026-08-12T12:00:00Z",
      closesAt: "2026-08-12T13:00:00Z"
    } : plan)
  };
  await page.clock.setFixedTime(new Date("2026-08-10T08:00:00+08:00"));
  await mockAdminApi(page, {
    intercept: async (route, request, path) => {
      if (path === "/v1/admin/activity-plans" && request.method() === "GET") {
        await route.fulfill({ json: conflictingOverview });
        return true;
      }
      return false;
    }
  });

  await page.goto("/admin/activity-plans");
  await page.locator(".fc-event").filter({ hasText: "周末生存挑战" }).click();
  const inspector = page.locator(".activity-plan-inspector");
  await expect(inspector.getByText(/与已发布企划《夏日建筑接力》重叠/)).toBeVisible();
  await expect(inspector.getByRole("button", { name: "发布企划" })).toBeDisabled();

  await page.setViewportSize({ width: 390, height: 844 });
  expect(await page.evaluate(() =>
    document.documentElement.scrollWidth <= document.documentElement.clientWidth
  )).toBe(true);
  await page.screenshot({ path: "../../../artifacts/admin-web-activity-plans-mobile.png", fullPage: true });
});

test("server editor recovers from a revision conflict without losing the draft", async ({ page }) => {
  const updates: Record<string, unknown>[] = [];
  await mockAdminApi(page, {
    intercept: async (route, request, path) => {
      if (path !== "/v1/admin/catalog/servers/activity" || request.method() !== "PUT") return false;
      const body = request.postDataJSON() as Record<string, unknown>;
      updates.push(body);
      if (updates.length === 1) {
        await route.fulfill({
          status: 409,
          json: {
            message: "服务器目录已被其他管理员修改，请刷新后重试。",
            current: { ...serverRecords[0], displayName: "活动服（他人修订）", revision: 2 }
          }
        });
      } else {
        await route.fulfill({
          json: { ...serverRecords[0], ...body, revision: 3 }
        });
      }
      return true;
    }
  });
  await page.goto("/admin/servers");
  await page.getByRole("button", { name: "编辑服务器" }).click();
  await page.getByLabel("显示名称").fill("活动服（我的草稿）");

  await page.getByRole("button", { name: "保存服务器" }).click();
  await expect(page.getByText(/服务器已有新修订 r2/)).toBeVisible();
  await expect(page.getByLabel("显示名称")).toHaveValue("活动服（我的草稿）");

  await page.getByRole("button", { name: "保存服务器" }).click();
  await expect(page.locator(".server-table")).toBeVisible();
  expect(updates).toHaveLength(2);
  expect(updates[0].expectedRevision).toBe(1);
  expect(updates[1].expectedRevision).toBe(2);
});

test("server directory opens the matching control target instead of changing visibility", async ({ page }) => {
  await mockAdminApi(page);
  await page.goto("/admin/servers");

  await page.getByRole("link", { name: "管理 活动服 的运行状态" }).click();

  await expect(page).toHaveURL(/\/admin\/control\?server=activity$/);
  await expect(page.locator(".control-detail-heading h3")).toHaveText("活动服");
});

test("server editor form fills the drawer without overlapping its chrome", async ({ page }) => {
  await mockAdminApi(page);
  await page.goto("/admin/servers");
  await page.getByRole("button", { name: "新增服务器" }).click();

  const drawer = page.locator(".vue-drawer");
  const form = drawer.locator(":scope > form");
  const header = form.locator(":scope > .drawer-header");
  const body = form.locator(":scope > .drawer-body");
  const footer = form.locator(":scope > .drawer-footer");
  await expect(drawer).toBeVisible();
  await expect(page.getByLabel("服务器 ID")).toBeVisible();

  const [drawerBox, formBox, headerBox, bodyBox, footerBox] = await Promise.all([
    drawer.boundingBox(),
    form.boundingBox(),
    header.boundingBox(),
    body.boundingBox(),
    footer.boundingBox()
  ]);
  expect(drawerBox && formBox && headerBox && bodyBox && footerBox).toBeTruthy();
  expect(Math.abs(formBox!.height - drawerBox!.height)).toBeLessThanOrEqual(1);
  expect(bodyBox!.height).toBeGreaterThan(300);
  expect(bodyBox!.y).toBeGreaterThanOrEqual(headerBox!.y + headerBox!.height - 1);
  expect(footerBox!.y).toBeGreaterThanOrEqual(bodyBox!.y + bodyBox!.height - 1);
  expect(footerBox!.y + footerBox!.height).toBeLessThanOrEqual(drawerBox!.y + drawerBox!.height + 1);
  await page.screenshot({ path: "../../../artifacts/admin-web-server-drawer-desktop.png", fullPage: true });

  await page.setViewportSize({ width: 390, height: 844 });
  const [mobileDrawerBox, mobileFormBox, mobileBodyBox] = await Promise.all([
    drawer.boundingBox(),
    form.boundingBox(),
    body.boundingBox()
  ]);
  expect(mobileDrawerBox && mobileFormBox && mobileBodyBox).toBeTruthy();
  expect(Math.abs(mobileFormBox!.height - mobileDrawerBox!.height)).toBeLessThanOrEqual(1);
  expect(mobileBodyBox!.height).toBeGreaterThan(300);
  await page.getByLabel("公告").scrollIntoViewIfNeeded();
  await expect(page.getByLabel("公告")).toBeVisible();
  await expect(page.getByRole("button", { name: "保存服务器" })).toBeVisible();
  await page.screenshot({ path: "../../../artifacts/admin-web-server-drawer-mobile.png", fullPage: true });
});

test("profile conflict does not claim stale data is the latest revision", async ({ page }) => {
  let detailRequests = 0;
  await mockAdminApi(page, {
    intercept: async (route, request, path) => {
      if (path === `/v1/admin/catalog/client-profiles/${profileSummary.id}` && request.method() === "GET") {
        detailRequests += 1;
        if (detailRequests > 1) {
          await route.fulfill({ status: 503, json: { detail: "最新档案暂时不可用。" } });
          return true;
        }
      }
      if (path === `/v1/admin/catalog/client-profiles/${profileSummary.id}` && request.method() === "PUT") {
        await route.fulfill({ status: 409, json: { message: "档案已被其他管理员修改。" } });
        return true;
      }
      return false;
    }
  });
  await page.goto("/admin/profiles");
  await page.getByRole("button", { name: "管理客户端档案" }).click();
  const profileDrawer = page.locator(".profile-drawer");
  await profileDrawer.getByLabel("显示名称").fill("活动服 NeoForge 新名称");
  await profileDrawer.getByRole("button", { name: "保存档案信息" }).click();

  await expect(page.getByText(/最新修订读取失败/)).toBeVisible();
  await expect(page.getByText(/已载入最新修订/)).toHaveCount(0);
});

test("access-rule conflict reports when the latest preview cannot be loaded", async ({ page }) => {
  let previewRequests = 0;
  await mockAdminApi(page, {
    intercept: async (route, request, path) => {
      if (path === `/v1/admin/users/${userSummary.userId}/access-preview` && request.method() === "GET") {
        previewRequests += 1;
        if (previewRequests > 1) {
          await route.fulfill({ status: 503, json: { detail: "最新权限预览暂时不可用。" } });
          return true;
        }
      }
      if (path === `/v1/admin/users/${userSummary.userId}/access-rules/activity` && request.method() === "PUT") {
        await route.fulfill({ status: 409, json: { message: "权限规则已被其他管理员修改。" } });
        return true;
      }
      return false;
    }
  });
  await page.goto("/admin/users");
  await page.getByRole("button", { name: "预览最终权限" }).click();
  await page.getByRole("button", { name: "编辑单服规则" }).click();
  await page.getByLabel("原因").fill("保留我的规则草稿");
  await page.getByRole("button", { name: "保存规则" }).click();

  await expect(page.getByText(/最新修订读取失败/)).toBeVisible();
  await expect(page.getByLabel("原因")).toHaveValue("保留我的规则草稿");
});

test("infrastructure servers cannot be restored or converted back to player servers", async ({ page }) => {
  await mockAdminApi(page);
  await page.goto("/admin/servers");
  await page.getByRole("button", { name: "已归档" }).click();

  await expect(page.getByRole("button", { name: "基础设施节点不能恢复" })).toBeDisabled();
  await page.getByRole("button", { name: "编辑服务器" }).click();
  await expect(page.getByLabel("服务器角色")).toBeDisabled();
  await expect(page.getByLabel("允许协议转换")).toBeDisabled();
});

test("mobile navigation remains scrollable without covering page content", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await mockAdminApi(page);
  await page.goto("/admin/servers");
  await expect(page.locator(".page-heading h1")).toHaveText("服务器目录");
  const nav = page.locator(".primary-nav");
  await expect(nav).toBeVisible();
  expect(await nav.evaluate(element => element.scrollWidth > element.clientWidth)).toBe(true);
  const headingBox = await page.locator(".page-heading").boundingBox();
  const topbarBox = await page.locator(".topbar").boundingBox();
  expect(headingBox && topbarBox && headingBox.y >= topbarBox.y + topbarBox.height).toBe(true);
  await page.screenshot({ path: "../../../artifacts/admin-web-mobile.png", fullPage: true });
});

test("package import review drawer remains contained on mobile", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  const reviewRecord: PackageImportMock = {
    ...completedPackageImport,
    status: "AwaitingReview",
    plan: null,
    manifestSha256: null,
    deploymentOperationId: null,
    completedAt: null,
    revision: 4
  };
  await mockAdminApi(page, {
    intercept: async (route, request, path) => {
      if (path === "/v1/admin/server-control/overview") {
        const stopped = controlOverview(1);
        stopped.targets[0] = { ...stopped.targets[0], online: false, processId: null };
        await route.fulfill({ json: stopped });
        return true;
      }
      if (path === "/v1/admin/package-imports" && request.method() === "GET") {
        await route.fulfill({ json: {
          imports: [reviewRecord],
          publisherAgentConnected: true,
          publisherAgentLastSeenAt: now
        } });
        return true;
      }
      if (path === `/v1/admin/package-imports/${packageImportId}` && request.method() === "GET") {
        await route.fulfill({ json: reviewRecord });
        return true;
      }
      return false;
    }
  });

  await page.goto("/admin/package-imports");
  await page.getByRole("button", { name: "查看整合包任务" }).click();
  const importDrawer = page.locator(".package-import-drawer");
  await expect(importDrawer.getByRole("heading", { name: "等待确认" })).toBeVisible();
  const dimensions = await importDrawer.evaluate(element => ({
    width: element.getBoundingClientRect().width,
    scrollWidth: element.scrollWidth,
    clientWidth: element.clientWidth
  }));
  expect(dimensions.width).toBeLessThanOrEqual(390);
  expect(dimensions.scrollWidth).toBe(dimensions.clientWidth);
  await importDrawer.evaluate(async element => {
    await Promise.all(
      element.getAnimations({ subtree: true }).map(animation => animation.finished)
    );
  });
  await page.screenshot({ path: "../../../artifacts/admin-web-package-import-mobile.png" });
});

test("long desktop pages scroll inside content while the sidebar remains available", async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 720 });
  const users = Array.from({ length: 48 }, (_, index) => ({
    ...userSummary,
    userId: `33333333-3333-3333-3333-${String(index).padStart(12, "0")}`,
    username: `player-${String(index).padStart(2, "0")}`,
    displayName: `测试玩家 ${index + 1}`,
    email: `player-${index}@example.test`
  }));
  await mockAdminApi(page, {
    intercept: async (route, request, path) => {
      if (path === "/v1/admin/users" && request.method() === "GET") {
        await route.fulfill({ json: users });
        return true;
      }
      return false;
    }
  });
  await page.goto("/admin/users");
  await expect(page.locator(".user-table tbody tr")).toHaveCount(users.length);

  const metrics = await page.evaluate(() => {
    const content = document.querySelector<HTMLElement>(".content");
    if (!content) throw new Error("content region missing");
    content.scrollTop = content.scrollHeight;
    return {
      documentScrollHeight: document.documentElement.scrollHeight,
      documentClientHeight: document.documentElement.clientHeight,
      bodyScrollHeight: document.body.scrollHeight,
      bodyClientHeight: document.body.clientHeight,
      contentScrollTop: content.scrollTop,
      contentScrollHeight: content.scrollHeight,
      contentClientHeight: content.clientHeight
    };
  });

  expect(metrics.documentScrollHeight).toBe(metrics.documentClientHeight);
  expect(metrics.bodyScrollHeight).toBe(metrics.bodyClientHeight);
  expect(metrics.contentScrollHeight).toBeGreaterThan(metrics.contentClientHeight);
  expect(metrics.contentScrollTop).toBeGreaterThan(0);
  await expect(page.locator(".sidebar-brand")).toBeVisible();
  await expect(page.locator(".primary-nav")).toBeVisible();
  await expect(page.locator(".sidebar-account")).toBeVisible();
  await page.screenshot({ path: "../../../artifacts/admin-web-users-long-desktop.png" });
});

test("all migrated routes remain contained on mobile", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await mockAdminApi(page);
  for (const [route, heading] of migratedRoutes) {
    await page.goto(`/admin/${route}`);
    await expect(page.locator(".page-heading h1")).toHaveText(heading);
    expect(await page.evaluate(() =>
      document.documentElement.scrollWidth <= document.documentElement.clientWidth
    )).toBe(true);
  }
});

test("all migrated routes render independently without browser errors", async ({ page }) => {
  const browserErrors: string[] = [];
  page.on("pageerror", error => browserErrors.push(error.message));
  page.on("console", message => {
    if (message.type() === "error") browserErrors.push(message.text());
  });
  await mockAdminApi(page);
  for (const [route, heading] of migratedRoutes) {
    await page.goto(`/admin/${route}`);
    await expect(page.locator(".page-heading h1")).toHaveText(heading);
    expect(await page.evaluate(() =>
      document.documentElement.scrollWidth <= document.documentElement.clientWidth
    )).toBe(true);
  }
  expect(browserErrors).toEqual([]);
});

test("redeemed admin ticket is removed before router initialization", async ({ page }) => {
  let redeemedTicket: string | null = null;
  let redeemCount = 0;
  await mockAdminApi(page, {
    intercept: async (route, request, path) => {
      if (path !== "/v1/admin-auth/redeem" || request.method() !== "POST") return false;
      redeemCount += 1;
      redeemedTicket = (request.postDataJSON() as { ticket?: string }).ticket ?? null;
      await route.fulfill({ json: {} });
      return true;
    }
  });

  await page.goto("/admin/servers#ticket=single-use-ticket");
  await expect(page.locator(".page-heading h1")).toHaveText("服务器目录");
  await expect.poll(() => new URL(page.url()).hash).toBe("");
  expect(redeemedTicket).toBe("single-use-ticket");
  expect(redeemCount).toBe(1);

  await page.reload();
  await expect(page.locator(".page-heading h1")).toHaveText("服务器目录");
  expect(redeemCount).toBe(1);
});

test("all migrated routes have no automated WCAG A or AA violations", async ({ page }) => {
  await mockAdminApi(page);
  for (const [route, heading] of migratedRoutes) {
    await page.goto(`/admin/${route}`);
    await expect(page.locator(".page-heading h1")).toHaveText(heading);
    const report = await new AxeBuilder({ page })
      .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"])
      .analyze();
    const blocking = report.violations.map(item => ({
        route,
        id: item.id,
        impact: item.impact,
        targets: item.nodes.map(node => node.target.join(" "))
      }));
    expect(blocking).toEqual([]);
  }
});
