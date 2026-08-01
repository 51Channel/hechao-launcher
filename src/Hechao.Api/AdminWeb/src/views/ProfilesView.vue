<script setup lang="ts">
import { computed, nextTick, onScopeDispose, reactive, ref } from "vue";
import { api, ApiError } from "@/api/client";
import type {
  ClientProfile,
  ProfileChannel,
  ProfileDetail,
  ProfileRelease,
  ReleaseChannel
} from "@/api/types";
import AppIcon from "@/components/AppIcon.vue";
import ConfirmDialog from "@/components/ConfirmDialog.vue";
import PageHeading from "@/components/PageHeading.vue";
import ResourceState from "@/components/ResourceState.vue";
import { registerPageRefresh } from "@/composables/usePageRefresh";
import { useResource } from "@/composables/useResource";
import { showToast } from "@/composables/useToast";
import { formatBytes, formatDateTime, shortHash } from "@/utils";

interface ChannelDraft {
  manifestSha256: string;
  rolloutPercentage: number;
}

type Confirmation =
  | { kind: "update-production"; channel: ProfileChannel; manifestSha256: string | null; version: string | null }
  | { kind: "rollback"; channel: ProfileChannel };

const channelOrder: ReleaseChannel[] = ["Test", "Gray", "Production"];
const profiles = useResource(signal =>
  api<ClientProfile[]>("/v1/admin/catalog/client-profiles", { signal })
);
const list = computed(() => profiles.data.value ?? []);
const createDialog = ref<HTMLDialogElement | null>(null);
const createForm = reactive({ id: "", displayName: "" });
const createBusy = ref(false);
const createError = ref("");
const drawer = ref<HTMLDialogElement | null>(null);
const detail = ref<ProfileDetail | null>(null);
const detailLoading = ref(false);
const detailError = ref("");
const detailGeneration = ref(0);
const metadata = reactive({ displayName: "", isActive: false });
const metadataBusy = ref(false);
const channelBusy = ref<ReleaseChannel | null>(null);
const channelDrafts = reactive<Record<ReleaseChannel, ChannelDraft>>({
  Test: { manifestSha256: "", rolloutPercentage: 100 },
  Gray: { manifestSha256: "", rolloutPercentage: 10 },
  Production: { manifestSha256: "", rolloutPercentage: 100 }
});
const manifestFile = ref<File | null>(null);
const manifestInput = ref<HTMLInputElement | null>(null);
const importBusy = ref(false);
const pendingConfirmation = ref<Confirmation | null>(null);
const confirmationBusy = ref(false);
const pendingRelease = ref<{ release: ProfileRelease; pausing: boolean } | null>(null);
const releaseBusy = ref(false);

const selectedProfile = computed(() => detail.value?.profile ?? null);
const orderedChannels = computed(() => {
  const profile = selectedProfile.value;
  if (!profile) return [];
  return channelOrder
    .map(channelName => channelFor(profile, channelName))
    .filter((channel): channel is ProfileChannel => channel !== null);
});
const metadataDirty = computed(() => {
  const profile = selectedProfile.value;
  return Boolean(profile) &&
    (metadata.displayName.trim() !== profile!.displayName || metadata.isActive !== profile!.isActive);
});

function channelText(channel: ReleaseChannel): string {
  return { Test: "测试", Gray: "灰度", Production: "正式" }[channel];
}

function channelDescription(channel: ReleaseChannel): string {
  return {
    Test: "仅管理员账号按稳定分桶命中，用于首轮验证。",
    Gray: "已登录玩家按稳定分桶命中，用于逐步扩大覆盖。",
    Production: "未命中测试和灰度时使用的正式兜底版本。"
  }[channel];
}

function channelFor(profile: ClientProfile, channel: ReleaseChannel): ProfileChannel | null {
  return profile.channels.find(item => item.channel === channel) ?? null;
}

function productionChannel(profile: ClientProfile): ProfileChannel | null {
  return channelFor(profile, "Production");
}

function applyDetail(value: ProfileDetail): void {
  detail.value = value;
  metadata.displayName = value.profile.displayName;
  metadata.isActive = value.profile.isActive;
  for (const channelName of channelOrder) {
    const channel = channelFor(value.profile, channelName);
    channelDrafts[channelName] = {
      manifestSha256: channel?.manifestSha256 ?? "",
      rolloutPercentage: channelName === "Production" ? 100 : channel?.rolloutPercentage ?? 0
    };
  }
}

async function refreshAll(): Promise<void> {
  await profiles.refresh();
  if (detail.value) await refreshSelected();
}

const unregister = registerPageRefresh(refreshAll);
onScopeDispose(unregister);
void profiles.refresh();

function openCreate(): void {
  createForm.id = "";
  createForm.displayName = "";
  createError.value = "";
  createDialog.value?.showModal();
  void nextTick(() => createDialog.value?.querySelector<HTMLInputElement>("input")?.focus());
}

async function createProfile(): Promise<void> {
  createBusy.value = true;
  createError.value = "";
  try {
    const created = await api<ProfileDetail>("/v1/admin/catalog/client-profiles", {
      method: "POST",
      body: { id: createForm.id.trim(), displayName: createForm.displayName.trim() }
    });
    createDialog.value?.close();
    await profiles.refresh();
    applyDetail(created);
    drawer.value?.showModal();
    showToast("客户端档案已创建");
  } catch (reason) {
    createError.value = reason instanceof Error ? reason.message : "创建失败。";
  } finally {
    createBusy.value = false;
  }
}

async function openProfile(profileId: string): Promise<void> {
  const generation = ++detailGeneration.value;
  detailLoading.value = true;
  detailError.value = "";
  if (!drawer.value?.open) drawer.value?.showModal();
  try {
    const result = await api<ProfileDetail>(
      `/v1/admin/catalog/client-profiles/${encodeURIComponent(profileId)}`
    );
    if (generation === detailGeneration.value) applyDetail(result);
  } catch (reason) {
    if (generation === detailGeneration.value) {
      detailError.value = reason instanceof Error ? reason.message : "档案加载失败。";
    }
  } finally {
    if (generation === detailGeneration.value) detailLoading.value = false;
  }
}

function closeProfile(): void {
  detailGeneration.value += 1;
  drawer.value?.close();
  detail.value = null;
  detailError.value = "";
  manifestFile.value = null;
  if (manifestInput.value) manifestInput.value.value = "";
}

async function refreshSelected(): Promise<boolean> {
  const profileId = detail.value?.profile.id;
  if (!profileId) return false;
  const generation = detailGeneration.value;
  try {
    const result = await api<ProfileDetail>(
      `/v1/admin/catalog/client-profiles/${encodeURIComponent(profileId)}`
    );
    if (generation !== detailGeneration.value || detail.value?.profile.id !== profileId) {
      return false;
    }
    applyDetail(result);
    await profiles.refresh();
    return true;
  } catch (reason) {
    if (generation === detailGeneration.value && detail.value?.profile.id === profileId) {
      detailError.value = reason instanceof Error ? reason.message : "刷新档案失败。";
    }
    return false;
  }
}

async function handleMutationError(reason: unknown): Promise<void> {
  const message = reason instanceof Error ? reason.message : "操作失败。";
  detailError.value = message;
  if (reason instanceof ApiError && reason.status === 409) {
    const refreshed = await refreshSelected();
    detailError.value = refreshed
      ? "数据已被其他管理员修改，已载入最新修订，请核对后重试。"
      : `${message} 最新修订读取失败，请关闭档案后重新打开。`;
  }
}

async function saveMetadata(): Promise<void> {
  const profile = selectedProfile.value;
  if (!profile) return;
  metadataBusy.value = true;
  detailError.value = "";
  try {
    const updated = await api<ProfileDetail>(
      `/v1/admin/catalog/client-profiles/${encodeURIComponent(profile.id)}`,
      {
        method: "PUT",
        body: {
          displayName: metadata.displayName.trim(),
          isActive: metadata.isActive,
          expectedRevision: profile.revision
        }
      }
    );
    applyDetail(updated);
    await profiles.refresh();
    showToast("客户端档案信息已保存");
  } catch (reason) {
    await handleMutationError(reason);
  } finally {
    metadataBusy.value = false;
  }
}

function selectManifest(event: Event): void {
  manifestFile.value = (event.target as HTMLInputElement).files?.[0] ?? null;
}

async function importRelease(): Promise<void> {
  const profile = selectedProfile.value;
  const file = manifestFile.value;
  if (!profile || !file) {
    detailError.value = "请选择已签名的 JSON 清单。";
    return;
  }
  importBusy.value = true;
  detailError.value = "";
  try {
    const updated = await api<ProfileDetail>(
      `/v1/admin/catalog/client-profiles/${encodeURIComponent(profile.id)}/releases`,
      {
        method: "POST",
        rawBody: await file.arrayBuffer(),
        headers: { "Content-Type": "application/vnd.hechao.signed-manifest+json" },
        timeoutMs: 30_000
      }
    );
    manifestFile.value = null;
    if (manifestInput.value) manifestInput.value.value = "";
    applyDetail(updated);
    await profiles.refresh();
    showToast("签名版本已验证并导入");
  } catch (reason) {
    await handleMutationError(reason);
  } finally {
    importBusy.value = false;
  }
}

async function updateChannel(
  channel: ProfileChannel,
  manifestSha256: string | null,
  rolloutPercentage: number
): Promise<void> {
  const profile = selectedProfile.value;
  if (!profile) return;
  channelBusy.value = channel.channel;
  detailError.value = "";
  try {
    const updated = await api<ProfileDetail>(
      `/v1/admin/catalog/client-profiles/${encodeURIComponent(profile.id)}` +
        `/channels/${encodeURIComponent(channel.channel)}`,
      {
        method: "PUT",
        body: { manifestSha256, rolloutPercentage, expectedRevision: channel.revision }
      }
    );
    applyDetail(updated);
    await profiles.refresh();
    showToast(`${channelText(channel.channel)}通道已更新`);
  } catch (reason) {
    await handleMutationError(reason);
  } finally {
    channelBusy.value = null;
  }
}

async function saveChannel(channel: ProfileChannel): Promise<void> {
  const draft = channelDrafts[channel.channel];
  if (channel.channel === "Production") {
    const release = detail.value?.releases.find(item => item.manifestSha256 === draft.manifestSha256);
    pendingConfirmation.value = {
      kind: "update-production",
      channel,
      manifestSha256: draft.manifestSha256 || null,
      version: release?.version ?? null
    };
    return;
  }
  await updateChannel(
    channel,
    draft.manifestSha256 || null,
    Number(draft.rolloutPercentage)
  );
}

function requestAssignment(release: ProfileRelease, channelName: ReleaseChannel): void {
  const profile = selectedProfile.value;
  const channel = profile ? channelFor(profile, channelName) : null;
  if (!channel) return;
  if (channelName === "Production") {
    pendingConfirmation.value = {
      kind: "update-production",
      channel,
      manifestSha256: release.manifestSha256,
      version: release.version
    };
    return;
  }
  const rollout = channel.rolloutPercentage > 0
    ? channel.rolloutPercentage
    : channelName === "Test" ? 100 : 10;
  void updateChannel(channel, release.manifestSha256, rollout);
}

function requestRollback(channel: ProfileChannel): void {
  pendingConfirmation.value = { kind: "rollback", channel };
}

async function executeConfirmation(): Promise<void> {
  const pending = pendingConfirmation.value;
  const profile = selectedProfile.value;
  if (!pending || !profile) return;
  confirmationBusy.value = true;
  detailError.value = "";
  try {
    let updated: ProfileDetail;
    if (pending.kind === "update-production") {
      updated = await api<ProfileDetail>(
        `/v1/admin/catalog/client-profiles/${encodeURIComponent(profile.id)}` +
          `/channels/${encodeURIComponent(pending.channel.channel)}`,
        {
          method: "PUT",
          body: {
            manifestSha256: pending.manifestSha256,
            rolloutPercentage: 100,
            expectedRevision: pending.channel.revision
          }
        }
      );
      showToast(pending.version ? `v${pending.version} 已设为正式版本` : "正式通道已清空");
    } else {
      updated = await api<ProfileDetail>(
        `/v1/admin/catalog/client-profiles/${encodeURIComponent(profile.id)}` +
          `/channels/${encodeURIComponent(pending.channel.channel)}/rollback`,
        { method: "POST", body: { expectedRevision: pending.channel.revision } }
      );
      showToast(`${channelText(pending.channel.channel)}通道已回滚`);
    }
    pendingConfirmation.value = null;
    applyDetail(updated);
    await profiles.refresh();
  } catch (reason) {
    pendingConfirmation.value = null;
    await handleMutationError(reason);
  } finally {
    confirmationBusy.value = false;
  }
}

async function setReleasePaused(payload: { reason: string }): Promise<void> {
  const pending = pendingRelease.value;
  const profile = selectedProfile.value;
  if (!pending || !profile) return;
  releaseBusy.value = true;
  detailError.value = "";
  try {
    const updated = await api<ProfileDetail>(
      `/v1/admin/catalog/client-profiles/${encodeURIComponent(profile.id)}` +
        `/releases/${encodeURIComponent(pending.release.manifestSha256)}/pause`,
      {
        method: "PUT",
        body: {
          isPaused: pending.pausing,
          reason: pending.pausing ? payload.reason : "",
          expectedRevision: pending.release.revision
        }
      }
    );
    pendingRelease.value = null;
    applyDetail(updated);
    await profiles.refresh();
    showToast(pending.pausing
      ? `v${pending.release.version} 已暂停并完成通道回滚`
      : `v${pending.release.version} 已恢复`);
  } catch (reason) {
    pendingRelease.value = null;
    await handleMutationError(reason);
  } finally {
    releaseBusy.value = false;
  }
}

function confirmationTitle(): string {
  const pending = pendingConfirmation.value;
  if (!pending) return "确认发布操作";
  return pending.kind === "update-production"
    ? "切换正式版本"
    : `回滚${channelText(pending.channel.channel)}通道`;
}

function confirmationMessage(): string {
  const pending = pendingConfirmation.value;
  if (!pending) return "";
  return pending.kind === "update-production"
    ? pending.version
      ? `正式通道将切换到 v${pending.version}，所有未命中测试和灰度的玩家都会使用该版本。`
      : "正式通道将被清空；档案可能因此无法继续启用，玩家也将无法解析正式版本。"
    : `${channelText(pending.channel.channel)}通道将回到当前版本之前最近的可用版本。`;
}
</script>

<template>
  <section class="view-section">
    <PageHeading
      title="客户端档案"
      description="管理不可变签名版本，并按测试、灰度和正式通道控制玩家更新。"
      :updated-at="profiles.lastUpdatedAt.value"
      :stale="Boolean(profiles.error.value)"
    >
      <template #actions>
        <span class="count-label">{{ list.length }} 个档案</span>
        <button class="button button-primary" type="button" @click="openCreate">
          <AppIcon name="plus" />新建档案
        </button>
      </template>
    </PageHeading>

    <ResourceState
      :loading="profiles.loading.value && !profiles.data.value"
      :error="profiles.data.value ? '' : profiles.error.value"
      :empty="list.length === 0"
      empty-title="还没有客户端档案"
      empty-message="先创建档案，再导入离线签名清单。"
      @retry="profiles.refresh"
    >
      <div class="table-frame" tabindex="0" aria-label="可滚动数据表">
        <table class="profile-table">
          <thead><tr><th>档案</th><th>正式版本</th><th>发布通道</th><th>版本数</th><th>状态</th><th class="actions-column">操作</th></tr></thead>
          <tbody>
            <tr v-for="profile in list" :key="profile.id">
              <td><div class="profile-name"><strong>{{ profile.displayName }}</strong><span>{{ profile.id }} · r{{ profile.revision }}</span></div></td>
              <td>
                <div class="meta-stack">
                  <strong>{{ productionChannel(profile)?.version ? `v${productionChannel(profile)?.version}` : "尚未发布" }}</strong>
                  <span>{{ productionChannel(profile)?.manifestSha256 ? `${formatBytes(profile.downloadBytes)} · ${shortHash(productionChannel(profile)?.manifestSha256)}` : "正式通道未分配" }}</span>
                </div>
              </td>
              <td><div class="profile-channel-summary"><span v-for="channelName in channelOrder" :key="channelName" class="channel-pill" :class="{ assigned: channelFor(profile, channelName)?.manifestSha256 }">{{ channelText(channelName) }} {{ channelFor(profile, channelName)?.manifestSha256 ? channelName === "Production" ? `v${channelFor(profile, channelName)?.version}` : `${channelFor(profile, channelName)?.rolloutPercentage}%` : "未分配" }}</span></div></td>
              <td>{{ profile.releaseCount }}</td>
              <td><span class="status-badge" :class="profile.isActive ? 'status-online' : 'status-archived'">{{ profile.isActive ? "启用" : "停用" }}</span></td>
              <td class="actions-column"><button class="icon-button" type="button" title="管理客户端档案" aria-label="管理客户端档案" @click="openProfile(profile.id)"><AppIcon name="pencil" /></button></td>
            </tr>
          </tbody>
        </table>
      </div>
    </ResourceState>

    <dialog ref="createDialog" class="confirm-dialog profile-create-dialog" @cancel.prevent="createDialog?.close()">
      <form @submit.prevent="createProfile">
        <div class="confirm-icon"><AppIcon name="package" /></div>
        <h2>新建客户端档案</h2>
        <p>档案 ID 创建后不可修改；新档案默认停用。</p>
        <div v-if="createError" class="inline-alert" role="alert"><AppIcon name="circle-alert" /><span>{{ createError }}</span></div>
        <label>档案 ID<input v-model="createForm.id" pattern="[a-z0-9][a-z0-9._-]{1,63}" maxlength="64" autocomplete="off" required><span>2 至 64 位小写字母、数字、点、下划线或短横线。</span></label>
        <label>显示名称<input v-model="createForm.displayName" maxlength="80" required></label>
        <div class="confirm-actions"><button class="button button-secondary" type="button" :disabled="createBusy" @click="createDialog?.close()">取消</button><button class="button button-primary" type="submit" :disabled="createBusy">{{ createBusy ? "创建中…" : "创建档案" }}</button></div>
      </form>
    </dialog>

    <dialog ref="drawer" class="drawer profile-drawer vue-drawer" @cancel.prevent="closeProfile">
      <div class="drawer-header">
        <div><span>{{ selectedProfile?.id || "客户端档案" }}</span><h2>{{ selectedProfile?.displayName || "正在读取档案" }}</h2></div>
        <button class="icon-button" type="button" aria-label="关闭" @click="closeProfile"><AppIcon name="x" /></button>
      </div>
      <div class="drawer-body">
        <div v-if="detailError" class="inline-alert" role="alert"><AppIcon name="circle-alert" /><span>{{ detailError }}</span></div>
        <ResourceState :loading="detailLoading && !detail" :error="!detail ? detailError : ''" @retry="selectedProfile && openProfile(selectedProfile.id)">
          <template v-if="detail">
            <section class="profile-manager-section">
              <div class="profile-manager-heading"><div><span>档案身份</span><strong>{{ detail.profile.id }}</strong></div><span>修订 r{{ detail.profile.revision }}</span></div>
              <div class="profile-metadata-grid"><label>显示名称<input v-model="metadata.displayName" maxlength="80" required></label><label class="checkbox-row"><input v-model="metadata.isActive" type="checkbox"><span>允许客户端目录使用本档案</span></label></div>
              <button class="button button-secondary profile-section-action" type="button" :disabled="metadataBusy || !metadataDirty" @click="saveMetadata"><AppIcon name="save" />{{ metadataBusy ? "保存中…" : "保存档案信息" }}</button>
            </section>

            <section class="profile-manager-section">
              <div class="profile-manager-heading"><div><span>签名清单</span><strong>导入不可变版本</strong></div></div>
              <p class="profile-manager-copy">后台会验证签名、档案 ID、文件摘要和清单大小；不会接受手工填写的版本元数据。</p>
              <div class="profile-import-row"><input ref="manifestInput" type="file" accept="application/json,.json" @change="selectManifest"><button class="button button-secondary" type="button" :disabled="importBusy || !manifestFile" @click="importRelease"><AppIcon name="upload" />{{ importBusy ? "验证中…" : "验证并导入" }}</button></div>
            </section>

            <section class="profile-manager-section">
              <div class="profile-manager-heading"><div><span>发布路由</span><strong>三个稳定通道</strong></div></div>
              <div class="profile-channel-list">
                <article v-for="channel in orderedChannels" :key="channel.channel" class="profile-channel-card">
                    <div class="profile-channel-heading"><div><strong>{{ channelText(channel.channel) }}通道</strong><span>{{ channelDescription(channel.channel) }}</span></div><span class="status-badge" :class="channel.manifestSha256 ? 'status-online' : 'status-archived'">{{ channel.version ? `v${channel.version}` : "未分配" }}</span></div>
                    <div class="profile-channel-controls">
                      <label>发布版本<select v-model="channelDrafts[channel.channel].manifestSha256"><option value="">不分配</option><option v-for="release in detail.releases.filter(item => !item.isPaused)" :key="release.manifestSha256" :value="release.manifestSha256">v{{ release.version }} · {{ shortHash(release.manifestSha256) }}</option></select></label>
                      <label>覆盖比例<input v-model.number="channelDrafts[channel.channel].rolloutPercentage" type="number" min="0" max="100" step="1" :disabled="channel.channel === 'Production'"></label>
                    </div>
                    <div class="profile-channel-actions"><span>通道修订 r{{ channel.revision }}</span><div><button class="button button-secondary" type="button" :disabled="!channel.manifestSha256 || channelBusy !== null" @click="requestRollback(channel)"><AppIcon name="rotate-ccw" />回滚</button><button class="button button-secondary" type="button" :disabled="channelBusy !== null" @click="saveChannel(channel)"><AppIcon name="save" />{{ channelBusy === channel.channel ? "保存中…" : "保存通道" }}</button></div></div>
                </article>
              </div>
            </section>

            <section class="profile-manager-section">
              <div class="profile-manager-heading"><div><span>版本历史</span><strong>{{ detail.releases.length }} 个不可变版本</strong></div></div>
              <div v-if="detail.releases.length" class="profile-release-list">
                <article v-for="release in detail.releases" :key="release.manifestSha256" class="profile-release-card" :class="{ paused: release.isPaused }">
                  <div class="profile-release-heading"><div><strong>v{{ release.version }}</strong><span>{{ formatDateTime(release.publishedAt) }} · r{{ release.revision }}</span></div><span class="status-badge" :class="release.isPaused ? 'status-maintenance' : 'status-online'">{{ release.isPaused ? "已暂停" : "可发布" }}</span></div>
                  <dl class="profile-release-facts"><div><dt>运行环境</dt><dd>{{ release.minecraftVersion }} · {{ release.loader }} {{ release.loaderVersion }}</dd></div><div><dt>Java</dt><dd>{{ release.javaVersion }}</dd></div><div><dt>资源</dt><dd>{{ formatBytes(release.downloadBytes) }} · {{ release.fileCount }} 个文件</dd></div><div><dt>导入人</dt><dd>{{ release.createdByDisplayName || "系统迁移" }}</dd></div></dl>
                  <code class="profile-release-hash" :title="release.manifestSha256">{{ release.manifestSha256 }}</code>
                  <p v-if="release.pauseReason" class="profile-release-pause-reason">暂停原因：{{ release.pauseReason }}</p>
                  <div class="profile-release-actions"><div v-if="!release.isPaused"><button v-for="channelName in channelOrder" :key="channelName" class="button button-secondary" type="button" @click="requestAssignment(release, channelName)">{{ channelName === "Production" ? "设为正式" : `发布到${channelText(channelName)}` }}</button></div><button class="button" :class="release.isPaused ? 'button-secondary' : 'button-danger'" type="button" @click="pendingRelease = { release, pausing: !release.isPaused }"><AppIcon :name="release.isPaused ? 'rotate-ccw' : 'archive'" />{{ release.isPaused ? "恢复版本" : "暂停版本" }}</button></div>
                </article>
              </div>
              <div v-else class="resource-state resource-empty"><AppIcon name="package" /><strong>尚未导入版本</strong><span>使用上方签名清单入口导入第一份版本。</span></div>
            </section>
          </template>
        </ResourceState>
      </div>
      <div class="drawer-footer"><span>所有写入均使用修订号并进入审计日志。</span><button class="button button-secondary" type="button" @click="closeProfile">完成</button></div>
    </dialog>

    <ConfirmDialog
      :open="Boolean(pendingConfirmation)"
      :title="confirmationTitle()"
      :message="confirmationMessage()"
      :confirm-label="pendingConfirmation?.kind === 'rollback' ? '确认回滚' : '确认设为正式'"
      :danger="pendingConfirmation?.kind === 'rollback'"
      :busy="confirmationBusy"
      @close="pendingConfirmation = null"
      @confirm="executeConfirmation"
    />
    <ConfirmDialog
      :open="Boolean(pendingRelease)"
      :title="pendingRelease?.pausing ? '暂停问题版本' : '恢复已暂停版本'"
      :message="pendingRelease ? pendingRelease.pausing ? `v${pendingRelease.release.version} 将停止分发，引用它的通道会自动回滚。` : `v${pendingRelease.release.version} 将恢复为可发布状态，但不会自动进入任何通道。` : ''"
      :confirm-label="pendingRelease?.pausing ? '确认暂停并回滚' : '确认恢复版本'"
      :danger="pendingRelease?.pausing"
      :require-reason="pendingRelease?.pausing"
      reason-label="暂停原因"
      :busy="releaseBusy"
      @close="pendingRelease = null"
      @confirm="setReleasePaused"
    />
  </section>
</template>
