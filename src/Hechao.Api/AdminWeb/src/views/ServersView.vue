<script setup lang="ts">
import { computed, nextTick, onScopeDispose, reactive, ref, watch } from "vue";
import { api, ApiError } from "@/api/client";
import type { AdminServer, ClientProfile, ControlOverview, ModLoaderKind, RuntimeSummary } from "@/api/types";
import { registerPageRefresh } from "@/composables/usePageRefresh";
import { usePolling } from "@/composables/usePolling";
import { useResource } from "@/composables/useResource";
import { showToast } from "@/composables/useToast";
import { formatRelativeTime, fromLocalDateTimeInput, statusText, tierText, toLocalDateTimeInput } from "@/utils";
import AppIcon from "@/components/AppIcon.vue";
import ConfirmDialog from "@/components/ConfirmDialog.vue";
import PageHeading from "@/components/PageHeading.vue";
import ResourceState from "@/components/ResourceState.vue";

type Filter = "visible" | "archived" | "all";
interface ServerForm {
  id: string; displayName: string; shortName: string; iconGlyph: string;
  status: "Online" | "Maintenance" | "Closed"; maxPlayers: number;
  minecraftVersion: string; loader: ModLoaderKind; minimumTier: "Member" | "Participant" | "Collaborator" | "Administrator";
  clientProfileId: string; velocityTarget: string; allowsProtocolTranslation: boolean;
  role: "Player" | "Infrastructure"; monitoringEnabled: boolean; sortOrder: number;
  isVisible: boolean; announcement: string; opensAt: string; closesAt: string; revision: number | null;
}

const servers = useResource(signal => api<AdminServer[]>("/v1/admin/catalog/servers", { signal }));
const profiles = ref<ClientProfile[]>([]);
const control = ref<ControlOverview | null>(null);
const runtime = ref<RuntimeSummary | null>(null);
const filter = ref<Filter>("visible");
const search = ref("");
const drawer = ref<HTMLDialogElement | null>(null);
const editing = ref<AdminServer | null>(null);
const saving = ref(false);
const formError = ref("");
const discoveryId = ref("");
const pendingVisibility = ref<AdminServer | null>(null);
const visibilityBusy = ref(false);

const blankForm = (): ServerForm => ({
  id: "", displayName: "", shortName: "", iconGlyph: "服", status: "Online", maxPlayers: 30,
  minecraftVersion: "1.21.11", loader: "Paper", minimumTier: "Member", clientProfileId: "",
  velocityTarget: "", allowsProtocolTranslation: false, role: "Player", monitoringEnabled: true,
  sortOrder: 100, isVisible: true, announcement: "", opensAt: "", closesAt: "", revision: null
});
const form = reactive<ServerForm>(blankForm());

const list = computed(() => (servers.data.value ?? [])
  .filter(item => filter.value === "all" || (filter.value === "visible" ? item.isVisible : !item.isVisible))
  .filter(item => !search.value.trim() || [item.id, item.displayName, item.velocityTarget, item.clientProfileId]
    .some(value => value.toLocaleLowerCase("zh-CN").includes(search.value.trim().toLocaleLowerCase("zh-CN"))))
  .sort((a, b) => a.sortOrder - b.sortOrder || a.id.localeCompare(b.id)));

const summary = computed(() => ({
  total: servers.data.value?.length ?? 0,
  online: servers.data.value?.filter(item => item.role === "Player" && item.isVisible && item.effectiveStatus === "Online").length ?? 0,
  maintenance: servers.data.value?.filter(item => item.role === "Player" && item.isVisible && item.status === "Maintenance").length ?? 0,
  archived: servers.data.value?.filter(item => item.role === "Player" && !item.isVisible).length ?? 0
}));
const infrastructureRoleLocked = computed(() => editing.value?.role === "Infrastructure");

const discovered = computed(() => {
  const ids = new Set((servers.data.value ?? []).map(item => item.id));
  return (control.value?.targets ?? []).filter(item => item.agentConnected && item.online && !ids.has(item.serverId));
});

function effectiveStatus(item: AdminServer): string {
  if (item.role === "Infrastructure") return "内部节点";
  if (!item.isVisible) return "已归档";
  if (item.status === "Online" && item.hasControlTarget) {
    if (!item.controlTargetFresh) return "服控失联";
    if (item.controlReportedOnline === false) return "服务已停止";
  }
  return statusText(item.effectiveStatus);
}

function statusClass(item: AdminServer): string {
  if (!item.isVisible || item.effectiveStatus === "Closed") return "status-archived";
  return item.effectiveStatus === "Online" ? "status-online" : "status-maintenance";
}

function resetForm(value: ServerForm): void { Object.assign(form, value); }

async function loadDrawerDependencies(): Promise<void> {
  const [profileResult, controlResult, runtimeResult] = await Promise.allSettled([
    api<ClientProfile[]>("/v1/admin/catalog/client-profiles"),
    api<ControlOverview>("/v1/admin/server-control/overview"),
    api<RuntimeSummary>("/v1/admin/server-runtime/summary")
  ]);
  if (profileResult.status === "fulfilled") profiles.value = profileResult.value;
  if (controlResult.status === "fulfilled") control.value = controlResult.value;
  if (runtimeResult.status === "fulfilled") runtime.value = runtimeResult.value;
  const failed = [profileResult, controlResult, runtimeResult]
    .filter(result => result.status === "rejected").length;
  if (failed > 0) showToast(`有 ${failed} 项辅助数据读取失败，保存前请人工核对表单。`, true);
}

async function openCreate(): Promise<void> {
  editing.value = null; formError.value = ""; discoveryId.value = ""; resetForm(blankForm());
  await loadDrawerDependencies();
  form.clientProfileId = profiles.value.find(item => item.isActive)?.id ?? "";
  await nextTick(); drawer.value?.showModal();
}

async function openEdit(item: AdminServer): Promise<void> {
  editing.value = item; formError.value = ""; discoveryId.value = "";
  resetForm({
    id: item.id, displayName: item.displayName, shortName: item.shortName, iconGlyph: item.iconGlyph,
    status: item.status, maxPlayers: item.maxPlayers, minecraftVersion: item.minecraftVersion, loader: item.loader,
    minimumTier: item.minimumTier, clientProfileId: item.clientProfileId, velocityTarget: item.velocityTarget,
    allowsProtocolTranslation: item.allowsProtocolTranslation, role: item.role, monitoringEnabled: item.monitoringEnabled,
    sortOrder: item.sortOrder, isVisible: item.isVisible, announcement: item.announcement,
    opensAt: toLocalDateTimeInput(item.opensAt), closesAt: toLocalDateTimeInput(item.closesAt), revision: item.revision
  });
  await loadDrawerDependencies();
  await nextTick(); drawer.value?.showModal();
}

function closeDrawer(): void { drawer.value?.close(); }

function inferSoftware(value: string | null | undefined): { version: string | null; loader: ModLoaderKind | null } {
  const source = value ?? "";
  const version = source.match(/\b\d+\.\d+(?:\.\d+)?\b/)?.[0] ?? null;
  const loader = /neoforge/i.test(source) ? "NeoForge" : /fabric/i.test(source) ? "Fabric"
    : /forge/i.test(source) ? "Forge" : /paper|purpur|spigot|bukkit/i.test(source) ? "Paper"
      : /vanilla/i.test(source) ? "Vanilla" : null;
  return { version, loader };
}

function applyDiscovery(): void {
  const target = discovered.value.find(item => item.serverId === discoveryId.value);
  if (!target) return;
  const observed = runtime.value?.targets.find(item => item.velocityTarget === target.serverId);
  const software = inferSoftware(observed?.softwareVersion);
  form.id = target.serverId;
  form.displayName = target.displayName;
  form.shortName = target.displayName.slice(0, 16);
  form.iconGlyph = target.displayName.slice(0, 1) || "服";
  form.status = "Online";
  form.maxPlayers = target.settings?.maxPlayers ?? observed?.maxPlayers ?? 30;
  form.velocityTarget = target.serverId;
  form.monitoringEnabled = Boolean(observed);
  if (software.version) form.minecraftVersion = software.version;
  if (software.loader) form.loader = software.loader;
}

function requestBody(): Record<string, unknown> {
  return {
    displayName: form.displayName.trim(), shortName: form.shortName.trim(), iconGlyph: form.iconGlyph.trim(),
    status: form.status, maxPlayers: form.maxPlayers, minecraftVersion: form.minecraftVersion.trim(), loader: form.loader,
    minimumTier: form.minimumTier, clientProfileId: form.clientProfileId, velocityTarget: form.velocityTarget.trim(),
    allowsProtocolTranslation: form.allowsProtocolTranslation, role: form.role, monitoringEnabled: form.monitoringEnabled,
    sortOrder: form.sortOrder, announcement: form.announcement.trim(), opensAt: fromLocalDateTimeInput(form.opensAt),
    closesAt: fromLocalDateTimeInput(form.closesAt)
  };
}

function serverFromConflict(reason: ApiError): AdminServer | null {
  if (!reason.payload || typeof reason.payload !== "object" || !("current" in reason.payload)) return null;
  const current = (reason.payload as { current?: unknown }).current;
  return current && typeof current === "object" && "id" in current && "revision" in current
    ? current as AdminServer
    : null;
}

async function save(): Promise<void> {
  saving.value = true; formError.value = "";
  try {
    if (editing.value) {
      await api(`/v1/admin/catalog/servers/${encodeURIComponent(form.id)}`, {
        method: "PUT", body: { ...requestBody(), expectedRevision: form.revision }
      });
    } else {
      await api("/v1/admin/catalog/servers", { method: "POST", body: { id: form.id.trim(), isVisible: form.isVisible, ...requestBody() } });
    }
    closeDrawer(); showToast(editing.value ? "服务器目录已更新" : "服务器已加入目录"); await servers.refresh();
  } catch (reason) {
    if (reason instanceof ApiError && reason.status === 409 && editing.value) {
      const current = serverFromConflict(reason);
      await servers.refresh();
      const refreshed = current ?? servers.data.value?.find(item => item.id === form.id) ?? null;
      if (refreshed) {
        editing.value = refreshed;
        form.revision = refreshed.revision;
        if (refreshed.role === "Infrastructure") {
          form.role = "Infrastructure";
          form.allowsProtocolTranslation = false;
        }
      }
      formError.value = refreshed
        ? `服务器已有新修订 r${refreshed.revision}。你的表单内容已保留，请核对后再次保存。`
        : "服务器已被其他管理员修改，请关闭表单并重新打开。";
    } else {
      formError.value = reason instanceof Error ? reason.message : "保存失败。";
    }
  } finally { saving.value = false; }
}

function requestVisibility(item: AdminServer): void {
  if (item.role === "Infrastructure" && !item.isVisible) {
    showToast("内部基础设施服务器不能恢复到玩家目录。", true);
    return;
  }
  pendingVisibility.value = item;
}

async function changeVisibility(): Promise<void> {
  const item = pendingVisibility.value;
  if (!item) return;
  visibilityBusy.value = true;
  try {
    await api(`/v1/admin/catalog/servers/${encodeURIComponent(item.id)}/visibility`, {
      method: "PUT", body: { isVisible: !item.isVisible, expectedRevision: item.revision }
    });
    showToast(item.isVisible ? "服务器已归档" : "服务器已恢复"); pendingVisibility.value = null; await servers.refresh();
  } catch (reason) {
    if (reason instanceof ApiError && reason.status === 409) {
      pendingVisibility.value = null;
      await servers.refresh();
      showToast("目录状态已被其他管理员修改，已载入最新修订。", true);
    } else {
      showToast(reason instanceof Error ? reason.message : "操作失败。", true);
    }
  }
  finally { visibilityBusy.value = false; }
}

watch(() => form.role, role => {
  if (role !== "Infrastructure") return;
  form.isVisible = false;
  form.allowsProtocolTranslation = false;
});

usePolling(servers.refresh, 5_000);
const unregister = registerPageRefresh(servers.refresh);
onScopeDispose(unregister);
</script>

<template>
  <section class="view-section">
    <PageHeading title="服务器目录" description="目录策略与物理服状态分别展示；停服后会自动关闭玩家入口。" :updated-at="servers.lastUpdatedAt.value" :stale="Boolean(servers.error.value)">
      <template #actions><button class="button button-primary" type="button" @click="openCreate"><AppIcon name="plus" />新增服务器</button></template>
    </PageHeading>
    <div class="summary-strip" aria-label="服务器目录汇总">
      <div><span>目录总数</span><strong>{{ summary.total }}</strong></div><div><span>当前开放</span><strong>{{ summary.online }}</strong></div>
      <div><span>维护中</span><strong>{{ summary.maintenance }}</strong></div><div><span>已归档</span><strong>{{ summary.archived }}</strong></div>
    </div>
    <div v-if="servers.error.value && servers.data.value" class="stale-banner" role="status"><AppIcon name="circle-alert" />自动刷新失败，当前展示上次成功数据。<button type="button" @click="servers.refresh">重试</button></div>
    <div class="toolbar">
      <label class="search-control"><AppIcon name="search" /><input v-model="search" type="search" placeholder="搜索名称、ID、入口或客户端档案"></label>
      <div class="segmented-control" role="group" aria-label="目录显示范围">
        <button v-for="item in ([['visible','已展示'],['archived','已归档'],['all','全部']] as const)" :key="item[0]" type="button" :class="{ active: filter === item[0] }" :aria-pressed="filter === item[0]" @click="filter = item[0]">{{ item[1] }}</button>
      </div>
    </div>
    <ResourceState :loading="servers.loading.value && !servers.data.value" :error="servers.data.value ? '' : servers.error.value" :empty="list.length === 0" empty-title="没有符合条件的服务器" @retry="servers.refresh">
      <div class="table-frame" tabindex="0" aria-label="可滚动数据表"><table class="server-table"><thead><tr><th>服务器</th><th>实际状态</th><th>运行环境</th><th>客户端档案</th><th>最低称号</th><th>排序</th><th class="actions-column">操作</th></tr></thead>
        <tbody><tr v-for="item in list" :key="item.id">
          <td><div class="server-cell"><div class="server-glyph">{{ item.iconGlyph }}</div><div><strong>{{ item.displayName }}</strong><span>{{ item.id }} · r{{ item.revision }}</span></div></div></td>
          <td><span class="status-badge" :class="statusClass(item)">{{ effectiveStatus(item) }}</span><small v-if="item.hasControlTarget">{{ formatRelativeTime(item.controlLastSeenAt) }}</small></td>
          <td><div class="meta-stack"><strong>{{ item.minecraftVersion }} · {{ item.loader }}</strong><span>{{ item.velocityTarget }}</span></div></td>
          <td>{{ item.clientProfileId || "—" }}</td><td>{{ tierText(item.minimumTier) }}</td><td>{{ item.sortOrder }}</td>
          <td class="actions-column"><div class="row-actions"><button class="icon-button" type="button" title="编辑服务器" aria-label="编辑服务器" @click="openEdit(item)"><AppIcon name="pencil" /></button><button class="icon-button" type="button" :title="item.role === 'Infrastructure' && !item.isVisible ? '基础设施节点不能恢复' : item.isVisible ? '归档服务器' : '恢复服务器'" :aria-label="item.role === 'Infrastructure' && !item.isVisible ? '基础设施节点不能恢复' : item.isVisible ? '归档服务器' : '恢复服务器'" :disabled="item.role === 'Infrastructure' && !item.isVisible" @click="requestVisibility(item)"><AppIcon :name="item.isVisible ? 'archive' : 'rotate-ccw'" /></button></div></td>
        </tr></tbody></table></div>
    </ResourceState>

    <dialog ref="drawer" class="drawer vue-drawer" @cancel.prevent="closeDrawer">
      <form @submit.prevent="save">
        <header class="drawer-header"><div><span>服务器目录</span><h2>{{ editing ? "编辑服务器" : "新增服务器" }}</h2></div><button class="icon-button" type="button" aria-label="关闭" @click="closeDrawer"><AppIcon name="x" /></button></header>
        <div class="drawer-body">
          <div v-if="formError" class="inline-alert" role="alert"><AppIcon name="circle-alert" /><span>{{ formError }}</span></div>
          <section v-if="!editing" class="server-discovery"><div class="server-discovery-heading"><div><span>实时服控</span><strong>检测到的运行中服务器</strong></div><span>{{ discovered.length }} 个可添加</span></div>
            <label><span>选择服务器</span><select v-model="discoveryId" @change="applyDiscovery"><option value="">{{ discovered.length ? "选择一个目标" : "没有可添加的在线目标" }}</option><option v-for="item in discovered" :key="item.serverId" :value="item.serverId">{{ item.displayName }} · {{ item.agentId }}:{{ item.port }}</option></select></label>
            <p>自动识别只负责预填，客户端档案、加载器和 Velocity 入口仍需人工核对。</p>
          </section>
          <div class="form-grid">
            <label>服务器 ID<input v-model="form.id" pattern="[a-z0-9][a-z0-9._-]{1,63}" maxlength="64" :readonly="Boolean(editing)" required></label>
            <label>显示名称<input v-model="form.displayName" maxlength="80" required></label>
            <label>短名称<input v-model="form.shortName" maxlength="12" required></label>
            <label>图标字符<input v-model="form.iconGlyph" maxlength="12" required></label>
            <label>目录策略<select v-model="form.status"><option value="Online">开放</option><option value="Maintenance">维护</option><option value="Closed">关闭</option></select></label>
            <label>最大人数<input v-model.number="form.maxPlayers" type="number" min="1" max="10000" required></label>
            <label>Minecraft 版本<input v-model="form.minecraftVersion" maxlength="32" required></label>
            <label>加载器<select v-model="form.loader"><option v-for="item in ['Vanilla','Paper','NeoForge','Fabric','Forge']" :key="item">{{ item }}</option></select></label>
            <label>最低称号<select v-model="form.minimumTier"><option value="Member">成员</option><option value="Participant">活动成员</option><option value="Collaborator">协作者</option><option value="Administrator">管理员</option></select></label>
            <label>客户端档案<select v-model="form.clientProfileId" required><option value="">无</option><option v-for="item in profiles.filter(x => x.isActive || x.id === form.clientProfileId)" :key="item.id" :value="item.id">{{ item.displayName }} · {{ item.version }}</option></select></label>
            <label>Velocity 目标<input v-model="form.velocityTarget" maxlength="64" required></label>
            <label>服务器角色<select v-model="form.role" :disabled="infrastructureRoleLocked"><option value="Player">玩家服务器</option><option value="Infrastructure">内部基础设施</option></select><span v-if="infrastructureRoleLocked">基础设施节点不能再转换为玩家服务器。</span></label>
            <label>排序<input v-model.number="form.sortOrder" type="number" min="-100000" max="100000" required></label>
            <label>开放时间<input v-model="form.opensAt" type="datetime-local"></label>
            <label>关闭时间<input v-model="form.closesAt" type="datetime-local"></label>
          </div>
          <label>公告<textarea v-model="form.announcement" maxlength="280" rows="3"></textarea></label>
          <label class="checkbox-row"><input v-model="form.monitoringEnabled" type="checkbox"><span>启用运行监控</span></label>
          <label class="checkbox-row"><input v-model="form.allowsProtocolTranslation" type="checkbox" :disabled="form.role === 'Infrastructure'"><span>允许协议转换</span></label>
          <label v-if="!editing" class="checkbox-row"><input v-model="form.isVisible" type="checkbox" :disabled="form.role === 'Infrastructure'"><span>创建后立即展示给玩家</span></label>
        </div>
        <footer class="drawer-footer"><span>{{ editing ? `当前修订 r${form.revision}` : "创建前请核对客户端档案与 Velocity 入口" }}</span><div><button class="button button-secondary" type="button" @click="closeDrawer">取消</button><button class="button button-primary" type="submit" :disabled="saving">{{ saving ? "保存中…" : "保存服务器" }}</button></div></footer>
      </form>
    </dialog>

    <ConfirmDialog :open="Boolean(pendingVisibility)" :title="pendingVisibility?.isVisible ? '归档服务器' : '恢复服务器'" :message="pendingVisibility ? `${pendingVisibility.displayName} 将${pendingVisibility.isVisible ? '从玩家目录隐藏' : '重新进入玩家目录'}，不会启停 Java 进程。` : ''" :confirm-label="pendingVisibility?.isVisible ? '确认归档' : '确认恢复'" :danger="Boolean(pendingVisibility?.isVisible)" :busy="visibilityBusy" @close="pendingVisibility = null" @confirm="changeVisibility" />
  </section>
</template>
