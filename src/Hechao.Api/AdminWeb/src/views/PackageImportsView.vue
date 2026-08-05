<script setup lang="ts">
import { computed, nextTick, onScopeDispose, reactive, ref } from "vue";
import { ApiError, api } from "@/api/client";
import type {
  AccessTier,
  ControlOverview,
  ControlTargetSummary,
  PackageImportAnalysis,
  PackageImportListResponse,
  PackageImportRecord,
  PackageImportStatus,
  PackageUploadAppendResponse
} from "@/api/types";
import AppIcon from "@/components/AppIcon.vue";
import ConfirmDialog from "@/components/ConfirmDialog.vue";
import PageHeading from "@/components/PageHeading.vue";
import ResourceState from "@/components/ResourceState.vue";
import { registerPageRefresh } from "@/composables/usePageRefresh";
import { usePolling } from "@/composables/usePolling";
import { useResource } from "@/composables/useResource";
import { showToast } from "@/composables/useToast";
import { formatBytes, formatDateTime, formatRelativeTime, shortHash, tierText } from "@/utils";

const uploadChunkBytes = 8 * 1024 * 1024;
const cancellableStatuses: PackageImportStatus[] = [
  "Uploading", "Uploaded", "Analyzing", "AwaitingReview", "QueuedForPublishing"
];
const activeStatuses: PackageImportStatus[] = [
  "Uploading", "Uploaded", "Analyzing", "QueuedForPublishing", "PublishingClient",
  "QueuedForDeployment", "DeployingServer", "Finalizing"
];
const statusLabels: Record<PackageImportStatus, string> = {
  Uploading: "上传中",
  Uploaded: "等待识别",
  Analyzing: "识别中",
  AwaitingReview: "等待确认",
  QueuedForPublishing: "等待客户端发布",
  PublishingClient: "客户端发布中",
  QueuedForDeployment: "等待服务端部署",
  DeployingServer: "服务端部署中",
  Finalizing: "正在收口",
  Completed: "已完成",
  Failed: "失败",
  Cancelled: "已取消"
};
const phaseDefinitions = [
  { label: "上传", statuses: ["Uploading", "Uploaded"] },
  { label: "识别", statuses: ["Analyzing", "AwaitingReview"] },
  { label: "客户端发布", statuses: ["QueuedForPublishing", "PublishingClient"] },
  { label: "服务端部署", statuses: ["QueuedForDeployment", "DeployingServer"] },
  { label: "测试通道", statuses: ["Finalizing", "Completed"] }
] as const;

interface ReviewDraft {
  profileId: string;
  profileDisplayName: string;
  version: string;
  targetServerId: string;
  preserveWorldData: boolean;
  syncServerCatalog: boolean;
  serverDisplayName: string;
  minimumTier: AccessTier;
  maximumMemoryGiB: number;
  confirmation: string;
}

const imports = useResource(signal =>
  api<PackageImportListResponse>("/v1/admin/package-imports", { signal })
);
const controls = useResource(signal =>
  api<ControlOverview>("/v1/admin/server-control/overview", { signal })
);
const selectedImportId = ref("");
const selectedImport = useResource(signal =>
  api<PackageImportRecord>(
    `/v1/admin/package-imports/${encodeURIComponent(selectedImportId.value)}`,
    { signal }
  )
);
const drawer = ref<HTMLDialogElement | null>(null);
const drawerBody = ref<HTMLElement | null>(null);
const fileInput = ref<HTMLInputElement | null>(null);
const resumeInput = ref<HTMLInputElement | null>(null);
const selectedFile = ref<File | null>(null);
const resumeTarget = ref<PackageImportRecord | null>(null);
const dragActive = ref(false);
const createBusy = ref(false);
const upload = reactive({
  importId: "",
  fileName: "",
  uploadedBytes: 0,
  totalBytes: 0,
  running: false,
  paused: false,
  error: ""
});
let uploadController: AbortController | null = null;
const pendingCancel = ref<PackageImportRecord | null>(null);
const cancelBusy = ref(false);
const cancelError = ref("");
const confirmBusy = ref(false);
const confirmError = ref("");
const reviewBaseline = ref("");
const review = reactive<ReviewDraft>({
  profileId: "",
  profileDisplayName: "",
  version: "",
  targetServerId: "activity",
  preserveWorldData: false,
  syncServerCatalog: true,
  serverDisplayName: "",
  minimumTier: "Participant",
  maximumMemoryGiB: 4,
  confirmation: ""
});

const list = computed(() => imports.data.value?.imports ?? []);
const publisherConnected = computed(() =>
  Boolean(imports.data.value?.publisherAgentConnected)
);
const activityTarget = computed(() =>
  controls.data.value?.targets.find(isPackageDeploymentTarget) ?? null
);
const awaitingReviewCount = computed(() =>
  list.value.filter(item => item.status === "AwaitingReview").length
);
const activeCount = computed(() =>
  list.value.filter(item => activeStatuses.includes(item.status)).length
);
const reviewDirty = computed(() => serializeReview() !== reviewBaseline.value);
const exactConfirmation = computed(() =>
  selectedImport.data.value
    ? `发布并部署 ${selectedImport.data.value.importId}`
    : ""
);
const reviewHasBlockingIssues = computed(() =>
  selectedImport.data.value?.analysis?.issues.some(issue => issue.severity === "Blocking") ?? true
);
const deploymentReady = computed(() => {
  const target = activityTarget.value;
  return publisherConnected.value && Boolean(
    target?.agentConnected && !target.online && !target.activeOperation
  );
});
const reviewValid = computed(() => {
  const memoryMiB = review.maximumMemoryGiB * 1024;
  return deploymentReady.value &&
    !reviewHasBlockingIssues.value &&
    /^[a-z0-9][a-z0-9._-]{1,63}$/.test(review.profileId) &&
    review.profileDisplayName.trim().length >= 2 &&
    /^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$/.test(review.version) &&
    review.targetServerId === "activity" &&
    review.serverDisplayName.trim().length >= 2 &&
    Number.isInteger(memoryMiB) && memoryMiB >= 1024 && memoryMiB % 256 === 0 &&
    memoryMiB <= (activityTarget.value?.settings?.maximumAllowedMemoryMiB ?? 0) &&
    review.confirmation.trim() === exactConfirmation.value;
});
const uploadPercentage = computed(() =>
  upload.totalBytes > 0
    ? Math.min(100, Math.round(upload.uploadedBytes / upload.totalBytes * 100))
    : 0
);

function isPackageDeploymentTarget(target: ControlTargetSummary): boolean {
  return target.serverId === "activity" &&
    target.agentId === "owl5" &&
    target.conflictGroup === "owl5-activity-slot" &&
    target.port === 25568 &&
    target.packageDeploymentEnabled === true;
}

function serializeReview(): string {
  return JSON.stringify({
    profileId: review.profileId,
    profileDisplayName: review.profileDisplayName,
    version: review.version,
    targetServerId: review.targetServerId,
    preserveWorldData: review.preserveWorldData,
    syncServerCatalog: review.syncServerCatalog,
    serverDisplayName: review.serverDisplayName,
    minimumTier: review.minimumTier,
    maximumMemoryGiB: review.maximumMemoryGiB
  });
}

function initializeReview(record: PackageImportRecord, force = false): void {
  if (record.status !== "AwaitingReview" || !record.analysis) return;
  if (!force && reviewBaseline.value && reviewDirty.value) return;
  const metadata = record.analysis.metadata;
  const targetMemory = activityTarget.value?.settings?.maximumMemoryMiB ?? 4096;
  const hardLimit = activityTarget.value?.settings?.maximumAllowedMemoryMiB ?? targetMemory;
  review.profileId = record.plan?.profileId ?? metadata.suggestedProfileId;
  review.profileDisplayName = record.plan?.profileDisplayName ?? metadata.displayName;
  review.version = record.plan?.version ?? metadata.version;
  review.targetServerId = "activity";
  review.preserveWorldData = record.plan?.preserveWorldData ?? false;
  review.syncServerCatalog = record.plan?.syncServerCatalog ?? true;
  review.serverDisplayName = record.plan?.serverDisplayName ?? metadata.displayName;
  review.minimumTier = record.plan?.minimumTier ?? "Participant";
  review.maximumMemoryGiB = Math.min(
    record.plan?.maximumMemoryMiB ?? targetMemory,
    hardLimit
  ) / 1024;
  review.confirmation = "";
  reviewBaseline.value = serializeReview();
  confirmError.value = "";
}

async function refreshSelected(): Promise<PackageImportRecord | null> {
  if (!selectedImportId.value) return null;
  const result = await selectedImport.refresh();
  if (result) initializeReview(result);
  return result;
}

async function refreshPage(): Promise<void> {
  const [, , detail] = await Promise.all([
    imports.refresh(),
    controls.refresh(),
    selectedImportId.value ? selectedImport.refresh() : Promise.resolve(null)
  ]);
  if (detail) initializeReview(detail);
}

const unregister = registerPageRefresh(refreshPage);
onScopeDispose(() => {
  unregister();
  uploadController?.abort();
});
usePolling(refreshPage, 3_000);

async function openImport(importId: string): Promise<void> {
  selectedImportId.value = importId;
  reviewBaseline.value = "";
  selectedImport.cancel();
  await nextTick();
  const result = await selectedImport.refresh();
  if (result) initializeReview(result, true);
  if (!drawer.value?.open) drawer.value?.showModal();
  await nextTick();
  if (drawerBody.value) drawerBody.value.scrollTop = 0;
}

function closeImport(): void {
  drawer.value?.close();
  selectedImport.cancel();
  selectedImportId.value = "";
  confirmError.value = "";
}

function chooseFile(): void {
  fileInput.value?.click();
}

function acceptFile(file: File | null): void {
  if (!file) return;
  const extension = file.name.toLowerCase();
  if (!extension.endsWith(".zip") && !extension.endsWith(".mrpack")) {
    upload.error = "只接受 ZIP 或 MRPACK 整合包。";
    return;
  }
  if (file.size < 1024) {
    upload.error = "整合包文件过小。";
    return;
  }
  selectedFile.value = file;
  upload.error = "";
}

function onFileSelected(event: Event): void {
  acceptFile((event.target as HTMLInputElement).files?.[0] ?? null);
}

function onDrop(event: DragEvent): void {
  dragActive.value = false;
  acceptFile(event.dataTransfer?.files?.[0] ?? null);
}

async function createUpload(): Promise<void> {
  const file = selectedFile.value;
  if (!file || upload.running) return;
  createBusy.value = true;
  upload.error = "";
  try {
    const record = await api<PackageImportRecord>("/v1/admin/package-imports/uploads", {
      method: "POST",
      body: { fileName: file.name, totalBytes: file.size },
      timeoutMs: 30_000
    });
    await imports.refresh();
    await openImport(record.importId);
    await uploadFile(record, file);
  } catch (reason) {
    upload.error = reason instanceof Error ? reason.message : "无法创建上传任务。";
  } finally {
    createBusy.value = false;
  }
}

function chooseResume(record: PackageImportRecord): void {
  if (upload.running) return;
  resumeTarget.value = record;
  if (resumeInput.value) {
    resumeInput.value.value = "";
    resumeInput.value.click();
  }
}

async function onResumeSelected(event: Event): Promise<void> {
  const file = (event.target as HTMLInputElement).files?.[0] ?? null;
  const record = resumeTarget.value;
  resumeTarget.value = null;
  if (!file || !record) return;
  if (file.name !== record.fileName || file.size !== record.expectedUploadBytes) {
    upload.error = "续传文件的名称或大小与原任务不一致。";
    return;
  }
  await openImport(record.importId);
  await uploadFile(record, file);
}

async function uploadFile(record: PackageImportRecord, file: File): Promise<void> {
  if (upload.running) return;
  upload.importId = record.importId;
  upload.fileName = record.fileName;
  upload.uploadedBytes = record.uploadedBytes;
  upload.totalBytes = record.expectedUploadBytes;
  upload.running = true;
  upload.paused = false;
  upload.error = "";
  uploadController = new AbortController();
  let offset = record.uploadedBytes;
  let conflictRecoveries = 0;
  try {
    while (offset < file.size) {
      const chunk = file.slice(offset, Math.min(file.size, offset + uploadChunkBytes));
      try {
        const result = await api<PackageUploadAppendResponse>(
          `/v1/admin/package-imports/${encodeURIComponent(record.importId)}/content`,
          {
            method: "PATCH",
            rawBody: chunk,
            headers: { "Upload-Offset": String(offset) },
            signal: uploadController.signal,
            timeoutMs: 120_000
          }
        );
        offset = result.uploadedBytes;
        upload.uploadedBytes = offset;
        conflictRecoveries = 0;
      } catch (reason) {
        const serverOffset = readConflictOffset(reason);
        if (serverOffset === null || serverOffset === offset || conflictRecoveries >= 2) {
          throw reason;
        }
        offset = serverOffset;
        upload.uploadedBytes = offset;
        conflictRecoveries += 1;
      }
    }

    const completed = await api<PackageImportRecord>(
      `/v1/admin/package-imports/${encodeURIComponent(record.importId)}/complete`,
      { method: "POST", body: {}, timeoutMs: 120_000 }
    );
    selectedImport.data.value = completed;
    selectedFile.value = null;
    if (fileInput.value) fileInput.value.value = "";
    upload.paused = false;
    showToast("整合包上传完成，正在自动识别");
    await refreshPage();
  } catch (reason) {
    if (!upload.paused) {
      upload.error = reason instanceof Error ? reason.message : "整合包上传失败。";
      showToast(upload.error, true);
    }
  } finally {
    upload.running = false;
    uploadController = null;
  }
}

function readConflictOffset(reason: unknown): number | null {
  if (!(reason instanceof ApiError) || reason.status !== 409 ||
      !reason.payload || typeof reason.payload !== "object") return null;
  const value = (reason.payload as { uploadedBytes?: unknown }).uploadedBytes;
  return typeof value === "number" && Number.isSafeInteger(value) &&
    value >= 0 && value <= upload.totalBytes
    ? value
    : null;
}

function pauseUpload(): void {
  if (!upload.running) return;
  upload.paused = true;
  uploadController?.abort();
  showToast("上传已暂停，可从任务列表继续");
}

function requestCancel(record: PackageImportRecord): void {
  pendingCancel.value = record;
  cancelError.value = "";
}

async function cancelImport(payload: { reason: string }): Promise<void> {
  const record = pendingCancel.value;
  if (!record) return;
  cancelBusy.value = true;
  cancelError.value = "";
  try {
    if (upload.running && upload.importId === record.importId) pauseUpload();
    const updated = await api<PackageImportRecord>(
      `/v1/admin/package-imports/${encodeURIComponent(record.importId)}/cancel`,
      {
        method: "POST",
        body: { expectedRevision: record.revision, reason: payload.reason }
      }
    );
    pendingCancel.value = null;
    if (selectedImportId.value === updated.importId) selectedImport.data.value = updated;
    await imports.refresh();
    showToast("整合包导入任务已取消");
  } catch (reason) {
    cancelError.value = reason instanceof Error ? reason.message : "取消任务失败。";
  } finally {
    cancelBusy.value = false;
  }
}

async function confirmImport(): Promise<void> {
  const record = selectedImport.data.value;
  if (!record || !reviewValid.value) return;
  confirmBusy.value = true;
  confirmError.value = "";
  try {
    const updated = await api<PackageImportRecord>(
      `/v1/admin/package-imports/${encodeURIComponent(record.importId)}/confirm`,
      {
        method: "POST",
        body: {
          expectedRevision: record.revision,
          profileId: review.profileId.trim(),
          profileDisplayName: review.profileDisplayName.trim(),
          version: review.version.trim(),
          targetServerId: review.targetServerId,
          preserveWorldData: review.preserveWorldData,
          syncServerCatalog: review.syncServerCatalog,
          serverDisplayName: review.serverDisplayName.trim(),
          minimumTier: review.minimumTier,
          maximumMemoryMiB: review.maximumMemoryGiB * 1024,
          confirmation: review.confirmation.trim()
        }
      }
    );
    selectedImport.data.value = updated;
    reviewBaseline.value = serializeReview();
    await imports.refresh();
    await nextTick();
    if (drawerBody.value) drawerBody.value.scrollTop = 0;
    showToast("客户端发布与停服部署已进入队列");
  } catch (reason) {
    confirmError.value = reason instanceof Error ? reason.message : "确认导入失败。";
    if (reason instanceof ApiError && reason.status === 409) await refreshSelected();
  } finally {
    confirmBusy.value = false;
  }
}

function statusClass(status: PackageImportStatus): string {
  if (status === "Completed") return "status-online";
  if (status === "Failed") return "status-maintenance";
  if (status === "Cancelled") return "status-archived";
  if (status === "AwaitingReview") return "status-warning";
  return "status-running";
}

function statusText(status: PackageImportStatus): string {
  return statusLabels[status];
}

function packageIdentity(record: PackageImportRecord): string {
  const metadata = record.analysis?.metadata;
  return metadata
    ? `${metadata.minecraftVersion} · ${metadata.loader} ${metadata.loaderVersion}`
    : `${formatBytes(record.uploadedBytes)} / ${formatBytes(record.expectedUploadBytes)}`;
}

function canCancel(record: PackageImportRecord): boolean {
  return cancellableStatuses.includes(record.status);
}

function currentPhaseIndex(status: PackageImportStatus): number {
  if (status === "Failed" || status === "Cancelled") return -1;
  return phaseDefinitions.findIndex(phase =>
    (phase.statuses as readonly string[]).includes(status)
  );
}

function phaseClass(record: PackageImportRecord, index: number): string {
  if (record.status === "Completed") return "completed";
  const current = currentPhaseIndex(record.status);
  if (current > index) return "completed";
  if (current === index) return "current";
  return "pending";
}

function issueText(severity: string): string {
  return { Blocking: "阻断", Warning: "警告", Information: "信息" }[severity] ?? severity;
}

function sideText(side: string): string {
  return { Client: "客户端", Server: "服务端", Shared: "共用" }[side] ?? side;
}

function analysisSummary(analysis: PackageImportAnalysis): string {
  return `${analysis.clientFileCount} 个客户端文件 · ${analysis.serverFileCount} 个服务端文件 · ${analysis.sharedFileCount} 个共用文件`;
}
</script>

<template>
  <section class="view-section package-import-view">
    <PageHeading
      title="整合包导入"
      description="识别客户端与服务端内容，发布签名客户端，并将服务端原子部署到活动槽。"
      :updated-at="imports.lastUpdatedAt.value"
      :stale="Boolean(imports.error.value || controls.error.value)"
    >
      <template #actions><span class="count-label">{{ list.length }} 个任务</span></template>
    </PageHeading>

    <div class="summary-strip package-summary-strip">
      <div><span>发布代理</span><strong :class="publisherConnected ? 'summary-good' : 'summary-bad'">{{ publisherConnected ? "在线" : "离线" }}</strong></div>
      <div><span>活动部署槽</span><strong :class="activityTarget?.agentConnected ? 'summary-good' : 'summary-bad'">{{ activityTarget?.agentConnected ? activityTarget.online ? "运行中" : "已停服" : "不可用" }}</strong></div>
      <div><span>等待确认</span><strong>{{ awaitingReviewCount }}</strong></div>
      <div><span>处理中</span><strong>{{ activeCount }}</strong></div>
    </div>

    <div v-if="imports.error.value || controls.error.value" class="stale-banner" role="status">
      <AppIcon name="circle-alert" />
      <span>{{ imports.error.value || controls.error.value }}</span>
      <button type="button" @click="refreshPage">重试</button>
    </div>

    <section class="package-import-tool" aria-labelledby="package-upload-title">
      <div class="package-upload-column">
        <div class="package-tool-heading">
          <div><span>新建任务</span><h2 id="package-upload-title">上传整合包</h2></div>
          <span>ZIP / MRPACK</span>
        </div>
        <input ref="fileInput" hidden type="file" accept=".zip,.mrpack,application/zip,application/octet-stream" aria-label="选择新的整合包文件" @change="onFileSelected">
        <input ref="resumeInput" hidden type="file" accept=".zip,.mrpack,application/zip,application/octet-stream" aria-label="选择原文件继续上传" @change="onResumeSelected">
        <div
          class="package-drop-zone"
          :class="{ active: dragActive, selected: selectedFile }"
          @dragenter.prevent="dragActive = true"
          @dragover.prevent="dragActive = true"
          @dragleave.prevent="dragActive = false"
          @drop.prevent="onDrop"
        >
          <AppIcon name="package" :size="28" />
          <div v-if="selectedFile"><strong>{{ selectedFile.name }}</strong><span>{{ formatBytes(selectedFile.size) }}</span></div>
          <div v-else><strong>拖放整合包</strong><span>单文件，支持中断后继续上传</span></div>
          <button class="button button-secondary" type="button" :disabled="upload.running" @click="chooseFile">
            <AppIcon name="plus" />选择文件
          </button>
        </div>
        <div v-if="upload.importId && (upload.running || upload.paused || upload.error)" class="package-upload-progress" aria-live="polite">
          <div><strong>{{ upload.fileName }}</strong><span>{{ formatBytes(upload.uploadedBytes) }} / {{ formatBytes(upload.totalBytes) }}</span></div>
          <progress :value="upload.uploadedBytes" :max="upload.totalBytes || 1">{{ uploadPercentage }}%</progress>
          <div><span>{{ upload.running ? `上传中 ${uploadPercentage}%` : upload.paused ? "已暂停" : "上传未完成" }}</span><button v-if="upload.running" class="button button-quiet" type="button" @click="pauseUpload"><AppIcon name="square" />暂停</button></div>
        </div>
        <div v-if="upload.error" class="inline-alert compact-alert" role="alert"><AppIcon name="circle-alert" /><span>{{ upload.error }}</span></div>
        <div class="package-upload-actions">
          <button class="button button-primary" type="button" :disabled="!selectedFile || createBusy || upload.running" @click="createUpload">
            <AppIcon name="package" />{{ createBusy ? "正在创建" : "上传并识别" }}
          </button>
        </div>
      </div>

      <aside class="package-readiness" aria-label="导入链路状态">
        <div class="package-tool-heading"><div><span>执行链路</span><h2>发布与部署前置</h2></div></div>
        <ul class="readiness-list">
          <li :class="{ ready: publisherConnected }"><AppIcon :name="publisherConnected ? 'check' : 'circle-alert'" /><div><strong>客户端发布代理</strong><span>{{ publisherConnected ? `在线 · ${formatRelativeTime(imports.data.value?.publisherAgentLastSeenAt)}` : "等待 Publisher 心跳" }}</span></div></li>
          <li :class="{ ready: Boolean(activityTarget?.packageDeploymentEnabled) }"><AppIcon :name="activityTarget?.packageDeploymentEnabled ? 'check' : 'circle-alert'" /><div><strong>owl5 活动部署能力</strong><span>{{ activityTarget?.packageDeploymentEnabled ? "activity · 127.0.0.1:25568" : "未发现受控部署目标" }}</span></div></li>
          <li :class="{ ready: Boolean(activityTarget?.agentConnected) }"><AppIcon :name="activityTarget?.agentConnected ? 'check' : 'circle-alert'" /><div><strong>服控代理</strong><span>{{ activityTarget?.agentConnected ? "心跳正常" : "代理离线" }}</span></div></li>
          <li :class="{ ready: Boolean(activityTarget && !activityTarget.online && !activityTarget.activeOperation) }"><AppIcon :name="activityTarget && !activityTarget.online && !activityTarget.activeOperation ? 'check' : 'circle-alert'" /><div><strong>活动服停服状态</strong><span>{{ activityTarget?.online ? "服务端仍在运行" : activityTarget?.activeOperation ? "存在进行中的服控操作" : "可以部署，完成后仍保持停服" }}</span></div></li>
        </ul>
      </aside>
    </section>

    <div class="package-list-heading"><div><span>最近任务</span><strong>导入记录</strong></div><span>自动刷新</span></div>
    <ResourceState
      :loading="imports.loading.value && !imports.data.value"
      :error="imports.data.value ? '' : imports.error.value"
      :empty="list.length === 0"
      empty-title="还没有整合包任务"
      empty-message="上传第一份 ZIP 或 MRPACK 后，识别结果会出现在这里。"
      @retry="imports.refresh"
    >
      <div class="table-frame" tabindex="0" aria-label="可滚动整合包任务表">
        <table class="package-import-table">
          <thead><tr><th>整合包</th><th>识别结果</th><th>客户端 / 服务端</th><th>状态</th><th>更新时间</th><th class="actions-column">操作</th></tr></thead>
          <tbody>
            <tr v-for="item in list" :key="item.importId">
              <td><div class="profile-name"><strong>{{ item.fileName }}</strong><span>{{ item.createdByDisplayName || "管理员" }} · r{{ item.revision }}</span></div></td>
              <td><div class="meta-stack"><strong>{{ item.analysis?.metadata.displayName || "等待识别" }}</strong><span>{{ packageIdentity(item) }}</span></div></td>
              <td><div class="meta-stack"><strong>{{ item.analysis?.client ? formatBytes(item.analysis.client.archiveBytes) : "未生成" }} / {{ item.analysis?.server ? formatBytes(item.analysis.server.archiveBytes) : "未生成" }}</strong><span>{{ item.analysis ? analysisSummary(item.analysis) : shortHash(item.sourceSha256) }}</span></div></td>
              <td><span class="status-badge" :class="statusClass(item.status)">{{ statusText(item.status) }}</span></td>
              <td>{{ formatDateTime(item.updatedAt) }}</td>
              <td class="actions-column"><div class="package-row-actions"><button v-if="item.status === 'Uploading'" class="icon-button" type="button" title="继续上传" aria-label="继续上传" :disabled="upload.running" @click="chooseResume(item)"><AppIcon name="play" /></button><button class="icon-button" type="button" title="查看任务" aria-label="查看整合包任务" @click="openImport(item.importId)"><AppIcon name="eye" /></button></div></td>
            </tr>
          </tbody>
        </table>
      </div>
    </ResourceState>

    <dialog ref="drawer" class="drawer vue-drawer package-import-drawer" @cancel.prevent="closeImport">
      <div class="drawer-header">
        <div><span>{{ selectedImport.data.value?.fileName || "整合包任务" }}</span><h2>{{ selectedImport.data.value ? statusText(selectedImport.data.value.status) : "正在读取" }}</h2></div>
        <button class="icon-button" type="button" aria-label="关闭" @click="closeImport"><AppIcon name="x" /></button>
      </div>
      <div ref="drawerBody" class="drawer-body">
        <ResourceState :loading="selectedImport.loading.value && !selectedImport.data.value" :error="selectedImport.data.value ? '' : selectedImport.error.value" @retry="refreshSelected">
          <template v-if="selectedImport.data.value">
            <div class="package-phase-track" aria-label="导入进度">
              <div v-for="(phase, index) in phaseDefinitions" :key="phase.label" :class="phaseClass(selectedImport.data.value, index)"><span>{{ index + 1 }}</span><strong>{{ phase.label }}</strong></div>
            </div>

            <div v-if="selectedImport.data.value.errorMessage" class="inline-alert compact-alert" role="alert"><AppIcon name="circle-alert" /><span><strong>{{ selectedImport.data.value.errorCode }}</strong>{{ selectedImport.data.value.errorMessage }}</span></div>

            <section v-if="selectedImport.data.value.analysis" class="package-detail-section">
              <div class="profile-manager-heading"><div><span>自动识别</span><strong>{{ selectedImport.data.value.analysis.metadata.displayName }}</strong></div><span>{{ selectedImport.data.value.analysis.layout }}</span></div>
              <dl class="package-fact-grid">
                <div><dt>Minecraft</dt><dd>{{ selectedImport.data.value.analysis.metadata.minecraftVersion }}</dd></div>
                <div><dt>加载器</dt><dd>{{ selectedImport.data.value.analysis.metadata.loader }} {{ selectedImport.data.value.analysis.metadata.loaderVersion }}</dd></div>
                <div><dt>Java</dt><dd>{{ selectedImport.data.value.analysis.metadata.javaMajorVersion }}</dd></div>
                <div><dt>版本</dt><dd>{{ selectedImport.data.value.analysis.metadata.version }}</dd></div>
                <div><dt>启动入口</dt><dd><code>{{ selectedImport.data.value.analysis.metadata.serverLaunchPath || "未识别" }}</code></dd></div>
                <div><dt>客户端</dt><dd>{{ selectedImport.data.value.analysis.client ? `${formatBytes(selectedImport.data.value.analysis.client.archiveBytes)} · ${selectedImport.data.value.analysis.client.fileCount} 文件` : "未识别" }}</dd></div>
                <div><dt>服务端</dt><dd>{{ selectedImport.data.value.analysis.server ? `${formatBytes(selectedImport.data.value.analysis.server.archiveBytes)} · ${selectedImport.data.value.analysis.server.fileCount} 文件` : "未识别" }}</dd></div>
              </dl>
              <div class="package-issue-list">
                <div v-if="selectedImport.data.value.analysis.issues.length === 0" class="package-clean-result"><AppIcon name="check" /><span>未发现阻断项</span></div>
                <article v-for="issue in selectedImport.data.value.analysis.issues" :key="`${issue.code}:${issue.path}`" :class="`issue-${issue.severity.toLowerCase()}`"><span>{{ issueText(issue.severity) }}</span><div><strong>{{ issue.message }}</strong><code v-if="issue.path">{{ issue.path }}</code></div></article>
              </div>
            </section>

            <section v-if="selectedImport.data.value.status === 'AwaitingReview' && selectedImport.data.value.analysis" class="package-detail-section package-review-section">
              <div class="profile-manager-heading"><div><span>人工确认</span><strong>客户端发布与停服部署</strong></div><span v-if="reviewDirty" class="dirty-indicator">有未提交更改</span></div>
              <div class="package-review-readiness">
                <span :class="{ ready: publisherConnected }"><AppIcon :name="publisherConnected ? 'check' : 'circle-alert'" />发布代理</span>
                <span :class="{ ready: Boolean(activityTarget?.agentConnected) }"><AppIcon :name="activityTarget?.agentConnected ? 'check' : 'circle-alert'" />服控代理</span>
                <span :class="{ ready: Boolean(activityTarget && !activityTarget.online) }"><AppIcon :name="activityTarget && !activityTarget.online ? 'check' : 'circle-alert'" />目标已停服</span>
                <span :class="{ ready: !reviewHasBlockingIssues }"><AppIcon :name="!reviewHasBlockingIssues ? 'check' : 'circle-alert'" />{{ reviewHasBlockingIssues ? "识别存在阻断" : "识别无阻断" }}</span>
              </div>
              <form class="package-review-form" @submit.prevent="confirmImport">
                <label>客户端档案 ID<input v-model="review.profileId" pattern="[a-z0-9][a-z0-9._-]{1,63}" maxlength="64" required></label>
                <label>客户端显示名称<input v-model="review.profileDisplayName" minlength="2" maxlength="80" required></label>
                <label>版本号<input v-model="review.version" placeholder="1.0.0" required></label>
                <label>部署目标<select v-model="review.targetServerId" required><option value="activity">activity · owl5:25568</option></select></label>
                <label>服务器显示名称<input v-model="review.serverDisplayName" minlength="2" maxlength="80" required></label>
                <label>最低称号<select v-model="review.minimumTier"><option value="Member">{{ tierText('Member') }}</option><option value="Participant">{{ tierText('Participant') }}</option><option value="Collaborator">{{ tierText('Collaborator') }}</option></select></label>
                <label>最大内存（GiB）<input v-model.number="review.maximumMemoryGiB" type="number" min="1" :max="(activityTarget?.settings?.maximumAllowedMemoryMiB || 1024) / 1024" step="0.25" required><span>硬上限 {{ formatBytes((activityTarget?.settings?.maximumAllowedMemoryMiB || 0) * 1024 * 1024) }}</span></label>
                <div class="package-review-options">
                  <label class="checkbox-row"><input v-model="review.preserveWorldData" type="checkbox"><span>保留当前活动服世界目录</span></label>
                  <label class="checkbox-row"><input v-model="review.syncServerCatalog" type="checkbox"><span>同步隐藏且关闭的服务器目录记录</span></label>
                </div>
                <label class="package-confirmation-field">精确确认<input v-model="review.confirmation" autocomplete="off" maxlength="80" required><span>请输入：{{ exactConfirmation }}</span></label>
                <div v-if="confirmError" class="inline-alert settings-error" role="alert"><AppIcon name="circle-alert" /><span>{{ confirmError }}</span></div>
                <div class="package-review-submit"><span>不会停止其他服务器，不会启动活动服，不会修改 Production 通道。</span><button class="button button-primary" type="submit" :disabled="!reviewValid || confirmBusy"><AppIcon name="package" />{{ confirmBusy ? "正在排队" : "发布并部署" }}</button></div>
              </form>
            </section>

            <section v-if="selectedImport.data.value.plan" class="package-detail-section">
              <div class="profile-manager-heading"><div><span>部署计划</span><strong>{{ selectedImport.data.value.plan.profileDisplayName }} v{{ selectedImport.data.value.plan.version }}</strong></div><span>{{ selectedImport.data.value.plan.targetServerId }}</span></div>
              <dl class="package-fact-grid">
                <div><dt>客户端档案</dt><dd>{{ selectedImport.data.value.plan.profileId }}</dd></div>
                <div><dt>最大内存</dt><dd>{{ formatBytes(selectedImport.data.value.plan.maximumMemoryMiB * 1024 * 1024) }}</dd></div>
                <div><dt>世界目录</dt><dd>{{ selectedImport.data.value.plan.preserveWorldData ? "保留" : "使用整合包内容" }}</dd></div>
                <div><dt>目录记录</dt><dd>{{ selectedImport.data.value.plan.syncServerCatalog ? "同步为隐藏且关闭" : "不变" }}</dd></div>
                <div><dt>客户端清单</dt><dd>{{ shortHash(selectedImport.data.value.manifestSha256) }}</dd></div>
                <div><dt>部署操作</dt><dd>{{ selectedImport.data.value.deploymentOperationId || "尚未创建" }}</dd></div>
              </dl>
            </section>

            <section v-if="selectedImport.data.value.analysis?.fileSamples.length" class="package-detail-section">
              <div class="profile-manager-heading"><div><span>文件抽样</span><strong>{{ selectedImport.data.value.analysis.fileSamples.length }} 项</strong></div></div>
              <div class="table-frame" tabindex="0" aria-label="可滚动文件样本表"><table class="package-file-table"><thead><tr><th>路径</th><th>归属</th><th>大小</th><th>摘要</th></tr></thead><tbody><tr v-for="file in selectedImport.data.value.analysis.fileSamples" :key="`${file.side}:${file.path}`"><td><code>{{ file.path }}</code></td><td>{{ sideText(file.side) }}</td><td>{{ formatBytes(file.size) }}</td><td><code>{{ shortHash(file.sha256) }}</code></td></tr></tbody></table></div>
            </section>
          </template>
        </ResourceState>
      </div>
      <div class="drawer-footer"><span>{{ selectedImport.data.value ? `任务 ${selectedImport.data.value.importId} · r${selectedImport.data.value.revision}` : "所有写入均进入审计日志" }}</span><div><button v-if="selectedImport.data.value && canCancel(selectedImport.data.value)" class="button button-danger" type="button" @click="requestCancel(selectedImport.data.value)"><AppIcon name="x" />取消任务</button><button class="button button-secondary" type="button" @click="closeImport">关闭</button></div></div>
    </dialog>

    <ConfirmDialog
      :open="Boolean(pendingCancel)"
      title="取消整合包导入"
      :message="pendingCancel ? `${pendingCancel.fileName} 将停止后续识别、发布或部署；已进入发布阶段的任务不能取消。` : ''"
      confirm-label="确认取消"
      danger
      require-reason
      reason-label="取消原因"
      :busy="cancelBusy"
      :error="cancelError"
      @close="pendingCancel = null"
      @confirm="cancelImport"
    />
  </section>
</template>
