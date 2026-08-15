<script setup lang="ts">
import { computed, nextTick, onScopeDispose, reactive, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import { api } from "@/api/client";
import type {
  ControlAction,
  ControlOperation,
  ControlOverview,
  ControlQueueResult,
  ControlTargetDetail,
  DeploymentSlotKind,
  QuickSettings
} from "@/api/types";
import AppIcon from "@/components/AppIcon.vue";
import ConfirmDialog from "@/components/ConfirmDialog.vue";
import PageHeading from "@/components/PageHeading.vue";
import ResourceState from "@/components/ResourceState.vue";
import { registerPageRefresh } from "@/composables/usePageRefresh";
import { usePolling } from "@/composables/usePolling";
import { useResource } from "@/composables/useResource";
import { showToast } from "@/composables/useToast";
import { formatDateTime, formatRelativeTime } from "@/utils";

interface SettingsDraft {
  maxPlayers: number;
  viewDistance: number;
  simulationDistance: number;
  difficulty: string;
  whiteList: boolean;
  initialMemoryGiB: number;
  maximumMemoryGiB: number;
}

interface PendingAction {
  action: ControlAction;
  serverId: string;
  displayName: string;
  conflictDisplayNames: string[];
  consoleCommand: string | null;
  settings: QuickSettings | null;
}

const route = useRoute();
const router = useRouter();
const overview = useResource(signal =>
  api<ControlOverview>("/v1/admin/server-control/overview", { signal })
);

function routeServerId(): string {
  const value = route.query.server;
  return (Array.isArray(value) ? value[0] : value)?.trim() ?? "";
}

const selectedServerId = ref(routeServerId());
const targetDetail = useResource(signal =>
  api<ControlTargetDetail>(
    `/v1/admin/server-control/targets/${encodeURIComponent(selectedServerId.value)}`,
    { signal }
  )
);
const settingsDraft = reactive<SettingsDraft>({
  maxPlayers: 20,
  viewDistance: 10,
  simulationDistance: 10,
  difficulty: "normal",
  whiteList: false,
  initialMemoryGiB: 1,
  maximumMemoryGiB: 2
});
const settingsBaseline = ref("");
const settingsError = ref("");
const queuedSettingsOperationId = ref("");
const queuedSettings = ref<QuickSettings | null>(null);
const command = ref("");
const pendingAction = ref<PendingAction | null>(null);
const actionBusy = ref(false);
const actionError = ref("");
const consoleOutput = ref<HTMLElement | null>(null);
const followConsole = ref(true);

function targetVisible(target: Pick<
  ControlTargetDetail["target"],
  "serverFilesPresent" | "deletionCleanupPending" | "activeOperation"
>): boolean {
  return target.serverFilesPresent || target.deletionCleanupPending || target.activeOperation !== null;
}

const targets = computed(() => (overview.data.value?.targets ?? []).filter(targetVisible));
const selectedTarget = computed(() => {
  const target = targetDetail.data.value?.target;
  return target?.serverId === selectedServerId.value &&
    targets.value.some(candidate => candidate.serverId === target.serverId)
    ? target
    : null;
});
const connectedAgentCount = computed(() =>
  new Set(targets.value.filter(target => target.agentConnected).map(target => target.agentId)).size
);
const onlineCount = computed(() => targets.value.filter(target => target.online).length);
const activeOperationCount = computed(() =>
  targets.value.filter(target => target.activeOperation !== null).length
);
const selectedOperations = computed(() => {
  const detail = targetDetail.data.value;
  return detail?.target.serverId === selectedServerId.value
    ? detail.recentOperations
    : [];
});
const conflicts = computed(() => {
  const target = selectedTarget.value;
  if (!target?.conflictGroup) return [];
  return targets.value.filter(candidate =>
    candidate.serverId !== target.serverId &&
    candidate.conflictGroup === target.conflictGroup &&
    candidate.online
  );
});
const controlBusy = computed(() => Boolean(selectedTarget.value?.activeOperation));
const hasMemorySettings = computed(() => {
  const settings = selectedTarget.value?.settings;
  return settings?.initialMemoryMiB != null &&
    settings.maximumMemoryMiB != null &&
    settings.maximumAllowedMemoryMiB != null;
});
const settingsEnabled = computed(() =>
  Boolean(selectedTarget.value?.agentConnected) &&
  !controlBusy.value &&
  hasMemorySettings.value &&
  !queuedSettingsOperationId.value
);
const settingsDirty = computed(() => serializeDraft() !== settingsBaseline.value);
const consoleEnabled = computed(() =>
  Boolean(selectedTarget.value?.agentConnected && selectedTarget.value.online) && !controlBusy.value
);

function serializeDraft(): string {
  return JSON.stringify({
    maxPlayers: settingsDraft.maxPlayers,
    viewDistance: settingsDraft.viewDistance,
    simulationDistance: settingsDraft.simulationDistance,
    difficulty: settingsDraft.difficulty,
    whiteList: settingsDraft.whiteList,
    initialMemoryGiB: settingsDraft.initialMemoryGiB,
    maximumMemoryGiB: settingsDraft.maximumMemoryGiB
  });
}

function formatMemoryMiB(value: number | null | undefined): string {
  if (value == null || !Number.isFinite(value)) return "未上报";
  return value >= 1024
    ? `${Number.isInteger(value / 1024) ? value / 1024 : (value / 1024).toFixed(2)} GiB`
    : `${value} MiB`;
}

function deploymentSlotKindText(kind: DeploymentSlotKind | null): string {
  if (!kind) return "活动（固定槽）";
  return {
    Activity: "活动",
    Survival: "生存",
    Pvp: "PVP",
    Minigame: "小游戏"
  }[kind];
}

function settingsMatch(left: QuickSettings | null, right: QuickSettings | null): boolean {
  return Boolean(left && right) &&
    left!.maxPlayers === right!.maxPlayers &&
    left!.viewDistance === right!.viewDistance &&
    left!.simulationDistance === right!.simulationDistance &&
    left!.difficulty === right!.difficulty &&
    left!.whiteList === right!.whiteList &&
    left!.initialMemoryMiB === right!.initialMemoryMiB &&
    left!.maximumMemoryMiB === right!.maximumMemoryMiB;
}

function syncSettings(force = false): void {
  const settings = selectedTarget.value?.settings;
  if (!settings || queuedSettingsOperationId.value || (!force && settingsDirty.value)) return;
  settingsDraft.maxPlayers = settings.maxPlayers;
  settingsDraft.viewDistance = settings.viewDistance;
  settingsDraft.simulationDistance = settings.simulationDistance;
  settingsDraft.difficulty = settings.difficulty;
  settingsDraft.whiteList = settings.whiteList;
  settingsDraft.initialMemoryGiB = (settings.initialMemoryMiB ?? 1024) / 1024;
  settingsDraft.maximumMemoryGiB = (settings.maximumMemoryMiB ?? 2048) / 1024;
  settingsBaseline.value = serializeDraft();
  settingsError.value = "";
}

async function syncControlRoute(serverId: string): Promise<void> {
  if (routeServerId() === serverId) return;
  const query = { ...route.query };
  if (serverId) query.server = serverId;
  else delete query.server;
  await router.replace({ name: "control", query });
}

async function selectTarget(serverId: string): Promise<void> {
  await syncControlRoute(serverId);
  if (serverId === selectedServerId.value && selectedTarget.value) return;
  selectedServerId.value = serverId;
  queuedSettingsOperationId.value = "";
  queuedSettings.value = null;
  followConsole.value = true;
  targetDetail.cancel();
  await nextTick();
  const result = await targetDetail.refresh();
  if (!result || result.target.serverId !== selectedServerId.value) return;
  inspectQueuedSettings(result);
  syncSettings(true);
  await nextTick();
  scrollConsoleToEnd();
}

function inspectQueuedSettings(result: ControlTargetDetail): void {
  const operationId = queuedSettingsOperationId.value;
  if (!operationId) return;
  const operation = result.recentOperations.find(item => item.operationId === operationId);
  const target = result.target.serverId === selectedServerId.value ? result.target : null;
  if (!operation || !target) return;
  if (operation.status === "Succeeded" && settingsMatch(target.settings, queuedSettings.value)) {
    queuedSettingsOperationId.value = "";
    queuedSettings.value = null;
    syncSettings(true);
  } else if (["Failed", "Cancelled"].includes(operation.status)) {
    queuedSettingsOperationId.value = "";
    queuedSettings.value = null;
    syncSettings(true);
    showToast(operation.resultMessage || "快捷设置未能应用。", true);
  }
}

async function refreshControl(): Promise<void> {
  const detailPromise = selectedServerId.value
    ? targetDetail.refresh()
    : Promise.resolve(null);
  const [overviewResult, detailResult] = await Promise.all([
    overview.refresh(),
    detailPromise
  ]);
  const availableTargets = (
    overviewResult?.targets ?? overview.data.value?.targets ?? []
  ).filter(targetVisible);
  const requestedServerId = routeServerId();
  const requestedTarget = availableTargets.find(
    target => target.serverId === requestedServerId
  );
  const currentTarget = availableTargets.find(
    target => target.serverId === selectedServerId.value
  );
  const nextTarget = requestedTarget ?? currentTarget ?? availableTargets[0] ?? null;
  const nextServerId = nextTarget?.serverId ?? "";

  if (requestedServerId && !requestedTarget) {
    await syncControlRoute(nextServerId);
    showToast(
      nextTarget
        ? `未找到服控目标 ${requestedServerId}，已切换到 ${nextTarget.displayName}。`
        : `未找到服控目标 ${requestedServerId}，当前没有可管理的服务器。`,
      true
    );
  } else if (requestedServerId !== nextServerId) {
    await syncControlRoute(nextServerId);
  }

  if (nextServerId !== selectedServerId.value) {
    selectedServerId.value = nextServerId;
    queuedSettingsOperationId.value = "";
    queuedSettings.value = null;
    targetDetail.cancel();
    await nextTick();
    const selectedResult = nextServerId ? await targetDetail.refresh() : null;
    if (selectedResult) inspectQueuedSettings(selectedResult);
    syncSettings(true);
    await nextTick();
    scrollConsoleToEnd();
    return;
  }

  if (detailResult) inspectQueuedSettings(detailResult);
  syncSettings(false);
}

const unregister = registerPageRefresh(refreshControl);
onScopeDispose(unregister);
usePolling(refreshControl, 3_000);

watch(selectedServerId, () => {
  settingsError.value = "";
  actionError.value = "";
});

watch(
  () => selectedTarget.value?.consoleTail,
  async () => {
    const output = consoleOutput.value;
    const previousScrollTop = output?.scrollTop ?? 0;
    await nextTick();
    if (!consoleOutput.value) return;
    if (followConsole.value) scrollConsoleToEnd();
    else consoleOutput.value.scrollTop = previousScrollTop;
  },
  { flush: "pre" }
);

function onConsoleScroll(): void {
  const output = consoleOutput.value;
  if (!output) return;
  followConsole.value = output.scrollHeight - output.scrollTop - output.clientHeight < 24;
}

function scrollConsoleToEnd(): void {
  const output = consoleOutput.value;
  if (output) output.scrollTop = output.scrollHeight;
}

function toggleFollowConsole(): void {
  if (followConsole.value) void nextTick(scrollConsoleToEnd);
}

function controlActionText(action: ControlAction): string {
  return {
    Start: "启动",
    Stop: "停止",
    Restart: "重启",
    ConsoleCommand: "控制台命令",
    ApplySettings: "快捷设置",
    DeployPackage: "部署整合包",
    DeleteServerFiles: "删除服务端文件",
    CreateDeploymentSlot: "创建部署槽"
  }[action];
}

function controlStatusText(status: ControlOperation["status"]): string {
  return {
    Pending: "等待代理",
    Running: "执行中",
    Succeeded: "已完成",
    Failed: "失败",
    Cancelled: "已取消"
  }[status];
}

function actionMessage(pending: PendingAction): string {
  return {
    Start: `启动 ${pending.displayName}`,
    Stop: `保存世界后正常停止 ${pending.displayName}`,
    Restart: `保存世界、停止并重新启动 ${pending.displayName}`,
    ConsoleCommand: `向 ${pending.displayName} 发送：${pending.consoleCommand}`,
    ApplySettings: `更新 ${pending.displayName} 的 server.properties 与 JVM 启动内存；运行中的服务不会自动重启。`,
    DeployPackage: `替换 ${pending.displayName} 的受控服务端目录；完成后保持停服。`,
    DeleteServerFiles: `永久删除 ${pending.displayName} 的整个受控运行目录，包括世界、模组、插件、配置和日志；VPS 外置备份不会被删除。`,
    CreateDeploymentSlot: `在受控根目录中创建 ${pending.displayName}；完成后保持停服。`
  }[pending.action];
}

function actionTitle(action: ControlAction): string {
  return {
    Start: "启动服务器",
    Stop: "停止服务器",
    Restart: "重启服务器",
    ConsoleCommand: "发送 Minecraft 命令",
    ApplySettings: "保存快捷设置",
    DeployPackage: "部署整合包",
    DeleteServerFiles: "永久删除服务端文件",
    CreateDeploymentSlot: "创建部署槽"
  }[action];
}

function validateSettings(): QuickSettings | null {
  const targetSettings = selectedTarget.value?.settings;
  if (!targetSettings?.maximumAllowedMemoryMiB) {
    settingsError.value = "代理尚未上报可管理的 JVM 内存上限。";
    return null;
  }
  const initialMemoryMiB = settingsDraft.initialMemoryGiB * 1024;
  const maximumMemoryMiB = settingsDraft.maximumMemoryGiB * 1024;
  const values = [initialMemoryMiB, maximumMemoryMiB];
  if (values.some(value => !Number.isInteger(value) || value < 512 || value % 256 !== 0)) {
    settingsError.value = "JVM 内存必须至少为 0.5 GiB，并以 0.25 GiB 为步长。";
    return null;
  }
  if (initialMemoryMiB > maximumMemoryMiB) {
    settingsError.value = "最大内存不能小于初始内存。";
    return null;
  }
  if (maximumMemoryMiB > targetSettings.maximumAllowedMemoryMiB) {
    settingsError.value = `最大内存不能超过 ${formatMemoryMiB(targetSettings.maximumAllowedMemoryMiB)}。`;
    return null;
  }
  if (settingsDraft.maxPlayers < 1 || settingsDraft.maxPlayers > 1000 ||
      settingsDraft.viewDistance < 2 || settingsDraft.viewDistance > 32 ||
      settingsDraft.simulationDistance < 2 || settingsDraft.simulationDistance > 32) {
    settingsError.value = "人数或区块距离超出允许范围。";
    return null;
  }
  settingsError.value = "";
  return {
    maxPlayers: Number(settingsDraft.maxPlayers),
    viewDistance: Number(settingsDraft.viewDistance),
    simulationDistance: Number(settingsDraft.simulationDistance),
    difficulty: settingsDraft.difficulty,
    whiteList: settingsDraft.whiteList,
    initialMemoryMiB,
    maximumMemoryMiB,
    maximumAllowedMemoryMiB: targetSettings.maximumAllowedMemoryMiB
  };
}

function requestAction(action: ControlAction): void {
  const target = selectedTarget.value;
  if (!target?.agentConnected || target.activeOperation) {
    showToast("该服务器当前不能执行控制动作。", true);
    return;
  }
  if (action === "DeleteServerFiles") {
    if (!target.serverDeletionEnabled) {
      showToast("该服务器未在 VPS 代理中开放文件删除权限。", true);
      return;
    }
    if (target.online) {
      showToast("请先正常停止服务器并等待状态刷新，再删除文件。", true);
      return;
    }
    if (!target.serverFilesPresent) {
      showToast("该服务器的运行目录已经不存在。", true);
      return;
    }
  }
  let consoleCommand: string | null = null;
  let settings: QuickSettings | null = null;
  if (action === "ConsoleCommand") {
    consoleCommand = command.value.trim();
    if (!consoleCommand) {
      showToast("请先输入 Minecraft 控制台命令。", true);
      return;
    }
    const prefix = consoleCommand.replace(/^\//, "").split(/\s+/, 1)[0];
    if (!target.allowedCommandPrefixes.includes(prefix)) {
      showToast(`命令前缀“${prefix}”不在本服白名单中。`, true);
      return;
    }
  } else if (action === "ApplySettings") {
    settings = validateSettings();
    if (!settings) return;
  }
  pendingAction.value = {
    action,
    serverId: target.serverId,
    displayName: target.displayName,
    conflictDisplayNames: ["Start", "Restart"].includes(action)
      ? conflicts.value.map(item => item.displayName)
      : [],
    consoleCommand,
    settings
  };
  actionError.value = "";
}

async function submitAction(payload: { reason: string; confirmation: string }): Promise<void> {
  const pending = pendingAction.value;
  if (!pending) return;
  actionBusy.value = true;
  actionError.value = "";
  try {
    const result = await api<ControlQueueResult>(
      `/v1/admin/server-control/targets/${encodeURIComponent(pending.serverId)}/operations`,
      {
        method: "POST",
        body: {
          action: pending.action,
          confirmation: payload.confirmation,
          reason: payload.reason,
          consoleCommand: pending.consoleCommand,
          settings: pending.settings
        }
      }
    );
    if (pending.action === "ApplySettings") {
      queuedSettingsOperationId.value = result.operation.operationId;
      queuedSettings.value = pending.settings;
    }
    if (pending.action === "ConsoleCommand") command.value = "";
    pendingAction.value = null;
    const stopped = result.automaticallyStoppingServerIds;
    showToast(stopped.length
      ? `操作已排队，将先关闭 ${stopped.join("、")}`
      : "服务器控制操作已安全排队");
    await refreshControl();
  } catch (reason) {
    actionError.value = reason instanceof Error ? reason.message : "控制操作提交失败。";
  } finally {
    actionBusy.value = false;
  }
}

function runQuickCommand(value: string): void {
  command.value = value;
  requestAction("ConsoleCommand");
}

function commandAllowed(value: string): boolean {
  const prefix = value.split(/\s+/, 1)[0];
  return selectedTarget.value?.allowedCommandPrefixes.includes(prefix) ?? false;
}

function operationResult(operation: ControlOperation): string {
  const parts = [operation.resultMessage || "等待代理执行"];
  if (operation.automaticallyStoppingServerIds.length) {
    parts.push(`自动关闭：${operation.automaticallyStoppingServerIds.join("、")}`);
  }
  return parts.join("；");
}
</script>

<template>
  <section class="view-section">
    <PageHeading
      title="服控面板"
      description="通过白名单代理执行结构化启停、快捷设置和 Minecraft 控制台命令。"
      :updated-at="overview.lastUpdatedAt.value"
      :stale="Boolean(overview.error.value || targetDetail.error.value)"
    >
      <template #actions><span v-if="overview.data.value" class="count-label">状态生成于 {{ formatDateTime(overview.data.value.generatedAt) }}</span></template>
    </PageHeading>

    <div v-if="overview.error.value && overview.data.value" class="inline-alert compact-alert" role="status"><AppIcon name="circle-alert" /><span>自动刷新失败，当前显示上次成功数据：{{ overview.error.value }}</span></div>
    <div v-if="targetDetail.error.value && selectedTarget" class="inline-alert compact-alert" role="status"><AppIcon name="circle-alert" /><span>单服详情刷新失败，当前显示上次成功数据：{{ targetDetail.error.value }}</span></div>

    <ResourceState
      :loading="overview.loading.value && !overview.data.value"
      :error="overview.data.value ? '' : overview.error.value"
      :empty="targets.length === 0"
      empty-title="没有受管服务器"
      empty-message="服控代理尚未上报任何目标。"
      @retry="refreshControl"
    >
      <div class="summary-strip control-summary" aria-label="服务器控制摘要">
        <div><span>受管目标</span><strong>{{ targets.length }}</strong></div>
        <div><span>在线代理</span><strong>{{ connectedAgentCount }}</strong></div>
        <div><span>运行中</span><strong>{{ onlineCount }}</strong></div>
        <div><span>执行中操作</span><strong>{{ activeOperationCount }}</strong></div>
      </div>

      <div class="control-layout">
        <aside class="control-target-pane" aria-label="受管服务器">
          <div class="control-pane-heading"><h3>服务器</h3><span>{{ targets.length }} 个目标</span></div>
          <div class="control-target-list">
            <button v-for="target in targets" :key="target.serverId" class="control-target-item" :class="{ active: target.serverId === selectedServerId }" type="button" @click="selectTarget(target.serverId)">
              <i class="control-target-marker" :class="!target.agentConnected ? 'offline' : target.online ? 'online' : 'stopped'"></i>
              <strong>{{ target.displayName }}</strong>
              <span>{{ target.online ? "运行中" : target.serverFilesPresent ? "已停止" : "文件已删除" }} · {{ target.agentConnected ? target.agentId : "代理离线" }} · {{ target.settings?.maximumMemoryMiB ? `Xmx ${formatMemoryMiB(target.settings.maximumMemoryMiB)}` : "内存未上报" }}</span>
            </button>
          </div>
        </aside>

        <div v-if="selectedTarget" class="control-detail">
          <header class="control-detail-heading">
            <div><span class="control-server-id">{{ selectedTarget.serverId }}</span><h3>{{ selectedTarget.displayName }}</h3><p>{{ selectedTarget.online ? `运行中 · PID ${selectedTarget.processId || "未上报"} · 端口 ${selectedTarget.port}` : `已停止 · 端口 ${selectedTarget.port}` }}　{{ selectedTarget.agentConnected ? `代理 ${selectedTarget.agentId} 在线` : `代理 ${selectedTarget.agentId} 离线` }}　最后上报 {{ formatRelativeTime(selectedTarget.lastSeenAt) }}</p></div>
            <div class="control-actions">
              <button class="button button-primary" type="button" :disabled="!selectedTarget.agentConnected || controlBusy || !selectedTarget.serverFilesPresent || (selectedTarget.online && conflicts.length === 0)" @click="requestAction('Start')"><AppIcon name="play" />启动</button>
              <button class="button button-danger" type="button" :disabled="!selectedTarget.agentConnected || controlBusy || !selectedTarget.online" @click="requestAction('Stop')"><AppIcon name="square" />停止</button>
              <button class="button button-secondary" type="button" :disabled="!selectedTarget.agentConnected || controlBusy || !selectedTarget.serverFilesPresent" @click="requestAction('Restart')"><AppIcon name="refresh-cw" />重启</button>
            </div>
          </header>

          <div v-if="selectedTarget.conflictGroup" class="control-conflict-notice">冲突组 {{ selectedTarget.conflictGroup }}：启动本服前会先正常关闭 {{ conflicts.length ? conflicts.map(item => item.displayName).join("、") : "同组中正在运行的其他服务器" }}，确认端口释放后才继续。</div>

          <dl class="control-detail-metrics" aria-label="服务器运行与资源信息">
            <div><dt>运行状态</dt><dd>{{ selectedTarget.online ? "运行中" : "已停止" }}</dd></div>
            <div><dt>控制代理</dt><dd>{{ selectedTarget.agentId }} · {{ selectedTarget.agentConnected ? "在线" : "离线" }}</dd></div>
            <div v-if="selectedTarget.dynamicDeploymentSlot || selectedTarget.serverId === 'activity'"><dt>槽类型</dt><dd>{{ deploymentSlotKindText(selectedTarget.deploymentSlotKind) }}</dd></div>
            <div><dt>端口 / PID</dt><dd>{{ selectedTarget.port }} / {{ selectedTarget.processId || "未上报" }}</dd></div>
            <div><dt>启动内存</dt><dd>{{ hasMemorySettings ? `Xms ${formatMemoryMiB(selectedTarget.settings?.initialMemoryMiB)} · Xmx ${formatMemoryMiB(selectedTarget.settings?.maximumMemoryMiB)}` : "未上报" }}</dd></div>
            <div><dt>单服上限</dt><dd>{{ formatMemoryMiB(selectedTarget.settings?.maximumAllowedMemoryMiB) }}</dd></div>
            <div><dt>服务端文件</dt><dd>{{ selectedTarget.serverFilesPresent ? selectedTarget.deletionCleanupPending ? "目录存在 · 有待清理文件" : "目录存在" : selectedTarget.deletionCleanupPending ? "已移除 · 后台清理中" : "已删除" }}</dd></div>
          </dl>

          <section v-if="selectedTarget.serverDeletionEnabled && (selectedTarget.serverFilesPresent || selectedTarget.deletionCleanupPending)" class="control-danger-zone" aria-labelledby="server-files-title">
            <div class="control-danger-copy">
              <AppIcon name="trash-2" />
              <div><h3 id="server-files-title">服务端文件</h3><p v-if="selectedTarget.serverFilesPresent">永久删除受控运行目录以释放 VPS 空间。服务器必须先停止；外置备份和 OSS 客户端不受影响。</p><p v-else>{{ selectedTarget.deletionCleanupPending ? "运行目录已移除，代理正在重试清理暂存文件。" : "运行目录已删除。以后仍可通过整合包部署重新创建这个目标。" }}</p></div>
            </div>
            <button v-if="selectedTarget.serverFilesPresent" class="button button-danger" type="button" :disabled="!selectedTarget.agentConnected || controlBusy || selectedTarget.online" @click="requestAction('DeleteServerFiles')"><AppIcon name="trash-2" />删除服务端文件</button>
          </section>

          <div class="control-workspace-grid">
            <section class="control-settings">
              <div class="control-subheading"><div><h3>快捷设置</h3><p>写入 server.properties 与受管 JVM 参数；内存下次启动生效。</p></div><span v-if="settingsDirty" class="dirty-indicator">有未保存更改</span></div>
              <form class="control-settings-form" @submit.prevent="requestAction('ApplySettings')">
                <label>最大玩家数<input v-model.number="settingsDraft.maxPlayers" type="number" min="1" max="1000" required></label>
                <label>视距<input v-model.number="settingsDraft.viewDistance" type="number" min="2" max="32" required></label>
                <label>模拟距离<input v-model.number="settingsDraft.simulationDistance" type="number" min="2" max="32" required></label>
                <label>难度<select v-model="settingsDraft.difficulty" required><option value="peaceful">和平</option><option value="easy">简单</option><option value="normal">普通</option><option value="hard">困难</option></select></label>
                <label>初始内存（GiB）<input v-model.number="settingsDraft.initialMemoryGiB" type="number" min="0.5" :max="(selectedTarget.settings?.maximumAllowedMemoryMiB || 65536) / 1024" step="0.25" required></label>
                <label>最大内存（GiB）<input v-model.number="settingsDraft.maximumMemoryGiB" type="number" min="0.5" :max="(selectedTarget.settings?.maximumAllowedMemoryMiB || 65536) / 1024" step="0.25" required></label>
                <p class="control-memory-limit-hint">{{ hasMemorySettings ? `单服最大可设 ${formatMemoryMiB(selectedTarget.settings?.maximumAllowedMemoryMiB)}；运行中的服务不会自动重启。` : "代理尚未上报可管理的 JVM 内存参数。" }}</p>
                <label class="checkbox-row control-whitelist"><input v-model="settingsDraft.whiteList" type="checkbox"><span>启用服务器白名单</span></label>
                <div v-if="settingsError" class="inline-alert settings-error" role="alert"><AppIcon name="circle-alert" /><span>{{ settingsError }}</span></div>
                <div class="settings-actions"><button class="button button-quiet" type="button" :disabled="!settingsDirty || Boolean(queuedSettingsOperationId)" @click="syncSettings(true)">放弃更改</button><button class="button button-secondary" type="submit" :disabled="!settingsEnabled || !settingsDirty"><AppIcon name="save" />{{ queuedSettingsOperationId ? "等待代理应用" : "保存快捷设置" }}</button></div>
              </form>
            </section>

            <section class="control-terminal">
              <div class="control-subheading"><div><h3>Minecraft 控制台</h3><p>{{ selectedTarget.allowedCommandPrefixes.length ? `允许命令：${selectedTarget.allowedCommandPrefixes.join("、")}` : "本机未开放控制台命令" }}</p></div><label class="console-follow"><input v-model="followConsole" type="checkbox" @change="toggleFollowConsole"><span>跟随末尾</span></label></div>
              <div class="console-meta"><span>{{ selectedTarget.consoleCapturedAt ? `日志 ${formatRelativeTime(selectedTarget.consoleCapturedAt)}` : "暂无日志" }}</span></div>
              <pre ref="consoleOutput" class="control-console-output" tabindex="0" @scroll.passive="onConsoleScroll">{{ selectedTarget.consoleTail || "服务器尚未产生可读取的控制台日志。" }}</pre>
              <div class="control-quick-commands"><button type="button" :disabled="!consoleEnabled || !commandAllowed('list')" @click="runQuickCommand('list')">在线玩家</button><button type="button" :disabled="!consoleEnabled || !commandAllowed('save-all flush')" @click="runQuickCommand('save-all flush')">立即保存</button><button type="button" :disabled="!consoleEnabled || !commandAllowed('whitelist reload')" @click="runQuickCommand('whitelist reload')">重载白名单</button></div>
              <form class="control-command-form" @submit.prevent="requestAction('ConsoleCommand')"><input v-model="command" type="text" maxlength="240" autocomplete="off" placeholder="输入白名单内的 Minecraft 命令" :disabled="!consoleEnabled"><button class="button button-secondary" type="submit" :disabled="!consoleEnabled || !command.trim()"><AppIcon name="send" />发送</button></form>
            </section>
          </div>

          <section class="control-history">
            <div class="control-subheading"><div><h3>最近操作</h3><p>显示本服务器最近 20 条结构化控制记录。</p></div></div>
            <div v-if="selectedOperations.length" class="table-frame" tabindex="0" aria-label="可滚动数据表"><table class="control-history-table"><thead><tr><th>时间</th><th>动作</th><th>状态</th><th>结果</th></tr></thead><tbody><tr v-for="operation in selectedOperations" :key="operation.operationId"><td>{{ formatDateTime(operation.requestedAt) }}</td><td>{{ controlActionText(operation.action) }}</td><td><span class="status-badge" :class="operation.status === 'Succeeded' ? 'status-online' : operation.status === 'Failed' ? 'status-closed' : 'status-maintenance'">{{ controlStatusText(operation.status) }}</span></td><td>{{ operationResult(operation) }}</td></tr></tbody></table></div>
            <div v-else class="resource-state resource-empty"><strong>暂无控制记录</strong><span>本服务器还没有结构化控制操作。</span></div>
          </section>
        </div>
        <div v-else class="control-detail control-detail-state">
          <ResourceState
            :loading="targetDetail.loading.value"
            :error="targetDetail.error.value"
            @retry="targetDetail.refresh"
          />
        </div>
      </div>
    </ResourceState>

    <ConfirmDialog
      :open="Boolean(pendingAction)"
      :title="pendingAction ? actionTitle(pendingAction.action) : '确认服务器操作'"
      :message="pendingAction ? actionMessage(pendingAction) : ''"
      :confirm-label="pendingAction ? `确认${controlActionText(pendingAction.action)}` : '确认操作'"
      :danger="pendingAction?.action === 'Stop' || pendingAction?.action === 'Restart' || pendingAction?.action === 'DeleteServerFiles'"
      :busy="actionBusy"
      require-reason
      :confirmation-text="pendingAction ? pendingAction.action === 'DeleteServerFiles' ? `DELETE ${pendingAction.serverId}` : pendingAction.serverId : ''"
      :error="actionError"
      @close="pendingAction = null; actionError = ''"
      @confirm="submitAction"
    >
      <div v-if="pendingAction?.conflictDisplayNames.length" class="control-dialog-warning">将先自动保存并关闭：{{ pendingAction.conflictDisplayNames.join("、") }}。任何一个停止失败都会取消本次启动。</div>
      <div v-if="pendingAction?.action === 'DeleteServerFiles'" class="control-dialog-danger">此操作不可恢复。世界、模组、插件、配置和日志都会从该 VPS 的运行目录中删除。请先确认所需世界已有正式外置备份。</div>
    </ConfirmDialog>
  </section>
</template>
