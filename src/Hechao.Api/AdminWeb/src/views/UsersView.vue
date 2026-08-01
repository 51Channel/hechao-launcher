<script setup lang="ts">
import { computed, nextTick, onScopeDispose, reactive, ref } from "vue";
import { api, ApiError } from "@/api/client";
import type { AccessPreview, AccessPreviewServer, AccessTier, AdminUser, UserSecurity } from "@/api/types";
import { registerPageRefresh } from "@/composables/usePageRefresh";
import { useResource } from "@/composables/useResource";
import { showToast } from "@/composables/useToast";
import { accessReasonText, formatDateTime, fromLocalDateTimeInput, tierRank, tierText, toLocalDateTimeInput } from "@/utils";
import AppIcon from "@/components/AppIcon.vue";
import ConfirmDialog from "@/components/ConfirmDialog.vue";
import PageHeading from "@/components/PageHeading.vue";
import ResourceState from "@/components/ResourceState.vue";

type SecurityActionKind = "account-disable" | "account-enable" | "sessions-revoke-all" | "session-revoke" | "minecraft-ban" | "minecraft-unban";
interface SecurityAction { kind: SecurityActionKind; sessionId?: string; title: string; message: string; label: string; danger: boolean; expiry?: boolean }

const query = ref("");
const submittedQuery = ref("");
const users = useResource(signal => {
  const params = new URLSearchParams({ limit: "50" });
  if (submittedQuery.value) params.set("query", submittedQuery.value);
  return api<AdminUser[]>(`/v1/admin/users?${params}`, { signal });
});
const accessPreview = ref<AccessPreview | null>(null);
const previewLoading = ref(false);
let previewGeneration = 0;
const security = ref<UserSecurity | null>(null);
const securityDrawer = ref<HTMLDialogElement | null>(null);
const securityError = ref("");
const targetTier = ref<AccessTier>("Member");
const tierReason = ref("");
const securityBusy = ref(false);
const pendingSecurity = ref<SecurityAction | null>(null);
const securityActionReason = ref("");
const securityActionExpiry = ref("");
const securityActionDialog = ref<HTMLDialogElement | null>(null);
const ruleDialog = ref<HTMLDialogElement | null>(null);
const editingRule = ref<AccessPreviewServer | null>(null);
const ruleForm = reactive({ decision: "Allow" as "Allow" | "Deny", reason: "", expiresAt: "" });
const ruleError = ref(""); const ruleBusy = ref(false); const pendingDelete = ref<AccessPreviewServer | null>(null); const deleteBusy = ref(false);

const list = computed(() => users.data.value ?? []);
const unregister = registerPageRefresh(users.refresh); onScopeDispose(unregister); void users.refresh();

async function search(): Promise<void> {
  submittedQuery.value = query.value.trim();
  previewGeneration += 1;
  accessPreview.value = null;
  previewLoading.value = false;
  await users.refresh();
}
async function refreshUserData(userId: string): Promise<void> {
  const [securityResult, usersResult] = await Promise.all([
    api<UserSecurity>(`/v1/admin/users/${encodeURIComponent(userId)}/security`),
    users.refresh()
  ]);
  security.value = securityResult;
}

async function openPreview(userId: string): Promise<boolean> {
  const generation = ++previewGeneration;
  previewLoading.value = true;
  try {
    const result = await api<AccessPreview>(`/v1/admin/users/${encodeURIComponent(userId)}/access-preview`);
    if (generation !== previewGeneration) return false;
    accessPreview.value = result;
    await nextTick();
    document.querySelector(".access-preview-panel")?.scrollIntoView({ behavior: "auto", block: "start" });
    return true;
  }
  catch (reason) {
    if (generation === previewGeneration) {
      showToast(reason instanceof Error ? reason.message : "权限预览加载失败。", true);
    }
    return false;
  }
  finally { if (generation === previewGeneration) previewLoading.value = false; }
}

function closePreview(): void {
  previewGeneration += 1;
  accessPreview.value = null;
  previewLoading.value = false;
}

async function openSecurity(userId: string): Promise<void> {
  securityError.value = "";
  try {
    const result = await api<UserSecurity>(`/v1/admin/users/${encodeURIComponent(userId)}/security`);
    security.value = result;
    targetTier.value = result.user.accessTier; tierReason.value = "";
    await nextTick(); securityDrawer.value?.showModal();
  } catch (reason) { showToast(reason instanceof Error ? reason.message : "账号安全信息加载失败。", true); }
}

function closeSecurity(): void { securityDrawer.value?.close(); security.value = null; pendingSecurity.value = null; }

async function submitTierChange(): Promise<void> {
  if (!security.value || tierReason.value.trim().length < 4) { securityError.value = "等级变更原因必须为 4 到 500 个字符。"; return; }
  const userId = security.value.user.userId;
  securityBusy.value = true; securityError.value = "";
  try {
    await api(`/v1/admin/users/${encodeURIComponent(userId)}/access-tier`, { method: "PUT", body: { targetTier: targetTier.value, expectedPrimaryGroup: security.value.user.luckPermsPrimaryGroup, reason: tierReason.value.trim() } });
    showToast("全局等级变更已提交，等待大厅代理处理");
    closePreview();
    try {
      await refreshUserData(userId);
    } catch (reason) {
      securityError.value = reason instanceof Error ? `变更已提交，但状态刷新失败：${reason.message}` : "变更已提交，但状态刷新失败。";
    }
  } catch (reason) {
    securityError.value = reason instanceof Error ? reason.message : "提交失败。";
    if (reason instanceof ApiError && reason.status === 409) {
      try {
        await refreshUserData(userId);
        securityError.value = "等级状态已变化，已载入最新结果，请核对后重试。";
      } catch {
        securityError.value = "等级状态已变化，且最新状态读取失败，请稍后刷新。";
      }
    }
  }
  finally { securityBusy.value = false; }
}

function prepareSecurity(kind: SecurityActionKind, sessionId?: string): void {
  if (!security.value) return;
  const name = security.value.user.displayName;
  const configs: Record<SecurityActionKind, Omit<SecurityAction, "kind" | "sessionId">> = {
    "account-disable": { title: "停用赫朝账号", message: `停用“${name}”后，新登录、启动器、后台会话和进服授权会立即失效。`, label: "确认停用", danger: true },
    "account-enable": { title: "恢复赫朝账号", message: `恢复“${name}”的赫朝账号，UUID 封禁不会自动解除。`, label: "确认恢复", danger: false },
    "sessions-revoke-all": { title: "撤销全部会话", message: `撤销“${name}”的启动器设备、后台会话、登录票据、进服授权和论坛 Cookie。`, label: "全部撤销", danger: true },
    "session-revoke": { title: "撤销设备会话", message: `撤销设备会话 ${sessionId?.slice(-8) ?? ""}，该设备需要重新登录。`, label: "确认撤销", danger: true },
    "minecraft-ban": { title: "封禁 Minecraft UUID", message: `封禁 ${security.value.user.minecraftName || security.value.user.minecraftUuid} 后，客户端和进服都会被拒绝。`, label: "确认封禁", danger: true, expiry: true },
    "minecraft-unban": { title: "解除 UUID 封禁", message: `解除 ${security.value.user.minecraftName || security.value.user.minecraftUuid} 的 UUID 封禁。`, label: "确认解除", danger: false }
  };
  pendingSecurity.value = { kind, sessionId, ...configs[kind] }; securityActionReason.value = ""; securityActionExpiry.value = ""; securityError.value = "";
  void nextTick(() => securityActionDialog.value?.showModal());
}

async function executeSecurityAction(): Promise<void> {
  if (!security.value || !pendingSecurity.value || securityActionReason.value.trim().length < 4) return;
  const rawUserId = security.value.user.userId;
  const userId = encodeURIComponent(rawUserId); const action = pendingSecurity.value;
  let path = ""; let method = "POST"; let body: Record<string, unknown> = { reason: securityActionReason.value.trim() };
  switch (action.kind) {
    case "account-disable": path = `/v1/admin/users/${userId}/account/disable`; break;
    case "account-enable": path = `/v1/admin/users/${userId}/account/enable`; break;
    case "sessions-revoke-all": path = `/v1/admin/users/${userId}/sessions/revoke-all`; break;
    case "session-revoke": path = `/v1/admin/users/${userId}/sessions/${encodeURIComponent(action.sessionId ?? "")}/revoke`; break;
    case "minecraft-ban": path = `/v1/admin/users/${userId}/minecraft-ban`; method = "PUT"; body = { ...body, expiresAt: fromLocalDateTimeInput(securityActionExpiry.value), expectedRevision: null }; break;
    case "minecraft-unban": path = `/v1/admin/users/${userId}/minecraft-ban`; method = "DELETE"; body = { ...body, expectedRevision: security.value.minecraftIdentityBan?.revision }; break;
  }
  securityBusy.value = true; securityError.value = "";
  try {
    await api(path, { method, body }); securityActionDialog.value?.close(); pendingSecurity.value = null;
    closePreview();
    showToast("账号安全操作已完成");
    try {
      await refreshUserData(rawUserId);
    } catch (reason) {
      securityError.value = reason instanceof Error ? `操作已完成，但状态刷新失败：${reason.message}` : "操作已完成，但状态刷新失败。";
    }
  } catch (reason) {
    securityError.value = reason instanceof Error ? reason.message : "安全操作失败。";
    if (reason instanceof ApiError && reason.status === 409) {
      try { await refreshUserData(rawUserId); } catch { /* the original conflict remains actionable */ }
    }
  } finally { securityBusy.value = false; }
}

function openRule(item: AccessPreviewServer): void {
  editingRule.value = item; ruleForm.decision = item.rule?.decision ?? "Allow"; ruleForm.reason = item.rule?.reason ?? ""; ruleForm.expiresAt = toLocalDateTimeInput(item.rule?.expiresAt); ruleError.value = "";
  void nextTick(() => ruleDialog.value?.showModal());
}

async function saveRule(): Promise<void> {
  if (!accessPreview.value || !editingRule.value) return; ruleBusy.value = true; ruleError.value = "";
  try {
    await api(`/v1/admin/users/${encodeURIComponent(accessPreview.value.user.userId)}/access-rules/${encodeURIComponent(editingRule.value.serverId)}`, { method: "PUT", body: { decision: ruleForm.decision, reason: ruleForm.reason.trim(), expiresAt: fromLocalDateTimeInput(ruleForm.expiresAt), expectedRevision: editingRule.value.rule?.revision ?? null } });
    ruleDialog.value?.close(); showToast("单服权限规则已保存"); await Promise.all([openPreview(accessPreview.value.user.userId), users.refresh()]);
  } catch (reason) {
    ruleError.value = reason instanceof Error ? reason.message : "保存失败。";
    if (reason instanceof ApiError && reason.status === 409 && accessPreview.value && editingRule.value) {
      const userId = accessPreview.value.user.userId;
      const serverId = editingRule.value.serverId;
      const refreshSucceeded = await openPreview(userId);
      const refreshed = refreshSucceeded
        ? accessPreview.value?.servers.find(item => item.serverId === serverId) ?? null
        : null;
      if (refreshed) editingRule.value = refreshed;
      ruleError.value = !refreshSucceeded
        ? "规则已被其他管理员修改，但最新修订读取失败，请关闭表单后重新打开。"
        : refreshed
        ? "规则已被其他管理员修改。你的输入已保留，并已载入最新修订，请核对后重试。"
        : "该服务器已不在权限预览中，请关闭表单后重试。";
    }
  }
  finally { ruleBusy.value = false; }
}

function predictedAfterDelete(item: AccessPreviewServer): string {
  if (!accessPreview.value) return "将重新按账号状态、服务器状态与全局称号判断";
  const user = accessPreview.value.user;
  if (user.isDisabled) return "删除后仍拒绝：账号已停用";
  if (!user.minecraftUuid) return "删除后仍拒绝：未绑定正版身份";
  if (user.isMinecraftIdentityBanned) return "删除后仍拒绝：UUID 已封禁";
  if (!item.isVisible || item.effectiveStatus !== "Online") return "删除后仍拒绝：服务器未开放";
  return tierRank(user.accessTier) >= tierRank(item.minimumTier)
    ? "删除后将允许：全局称号满足"
    : "删除后将拒绝：全局称号不足";
}

async function deleteRule(): Promise<void> {
  if (!accessPreview.value || !pendingDelete.value?.rule) return; deleteBusy.value = true;
  try {
    const item = pendingDelete.value;
    const rule = item.rule;
    if (!rule) return;
    await api(`/v1/admin/users/${encodeURIComponent(accessPreview.value.user.userId)}/access-rules/${encodeURIComponent(item.serverId)}`, { method: "DELETE", body: { expectedRevision: rule.revision } });
    pendingDelete.value = null; ruleDialog.value?.close(); showToast("单服权限规则已清除"); await Promise.all([openPreview(accessPreview.value.user.userId), users.refresh()]);
  } catch (reason) {
    if (reason instanceof ApiError && reason.status === 409 && accessPreview.value && pendingDelete.value) {
      const userId = accessPreview.value.user.userId;
      const serverId = pendingDelete.value.serverId;
      const refreshSucceeded = await openPreview(userId);
      if (!refreshSucceeded) {
        showToast("规则已被其他管理员修改，但最新修订读取失败，请刷新权限预览后重试。", true);
        return;
      }
      const refreshed = accessPreview.value?.servers.find(item => item.serverId === serverId) ?? null;
      pendingDelete.value = refreshed?.rule ? refreshed : null;
      showToast(refreshed?.rule
        ? "规则已被其他管理员修改，已载入最新修订，请重新确认。"
        : "该规则已被其他管理员删除。", true);
    } else {
      showToast(reason instanceof Error ? reason.message : "删除失败。", true);
    }
  }
  finally { deleteBusy.value = false; }
}

function closeSecurityAction(): void {
  if (securityBusy.value) return;
  securityActionDialog.value?.close();
  pendingSecurity.value = null;
  securityError.value = "";
}
</script>

<template>
  <section class="view-section">
    <PageHeading title="玩家与权限" description="搜索赫朝账号，并预览服务器状态、称号和单服规则共同产生的最终结果。" :updated-at="users.lastUpdatedAt.value" :stale="Boolean(users.error.value)" />
    <form class="user-search-toolbar" @submit.prevent="search"><label class="search-control"><AppIcon name="search" /><input v-model="query" type="search" maxlength="80" placeholder="账号名、显示名、邮箱、Minecraft 名称或 UUID"></label><button class="button button-primary" type="submit" :disabled="users.loading.value"><AppIcon name="search" />搜索玩家</button></form>
    <ResourceState :loading="users.loading.value && !users.data.value" :error="users.data.value ? '' : users.error.value" :empty="list.length === 0" empty-title="没有找到玩家" @retry="users.refresh">
      <div class="table-frame" tabindex="0" aria-label="可滚动数据表"><table class="user-table"><thead><tr><th>赫朝账号</th><th>Minecraft 身份</th><th>全局称号</th><th>状态</th><th>单服规则</th><th class="actions-column">操作</th></tr></thead><tbody><tr v-for="user in list" :key="user.userId"><td><div class="profile-name"><strong>{{ user.displayName }}</strong><span>@{{ user.username }}</span></div></td><td><div class="meta-stack"><strong>{{ user.minecraftName || '尚未绑定' }}</strong><span>{{ user.minecraftUuid || '无 Minecraft 正版身份' }}</span></div></td><td>{{ tierText(user.accessTier) }}</td><td><span class="status-badge" :class="user.isDisabled || user.isMinecraftIdentityBanned ? 'status-closed' : 'status-online'">{{ user.isDisabled ? '已停用' : user.isMinecraftIdentityBanned ? 'UUID 已封禁' : '正常' }}</span></td><td>{{ user.activeRuleCount }}</td><td class="actions-column"><div class="row-actions"><button class="icon-button" type="button" title="预览最终权限" aria-label="预览最终权限" @click="openPreview(user.userId)"><AppIcon name="eye" /></button><button class="icon-button" type="button" title="管理账号安全" aria-label="管理账号安全" @click="openSecurity(user.userId)"><AppIcon name="key-round" /></button></div></td></tr></tbody></table></div>
    </ResourceState>

    <section v-if="accessPreview" class="access-preview-panel"><div class="section-heading"><div><h2>{{ accessPreview.user.displayName }} 的最终权限</h2><p>@{{ accessPreview.user.username }} · {{ tierText(accessPreview.user.accessTier) }}</p></div><button class="icon-button" type="button" aria-label="关闭权限预览" @click="closePreview"><AppIcon name="x" /></button></div><div class="table-frame" tabindex="0" aria-label="可滚动数据表"><table class="access-table"><thead><tr><th>服务器</th><th>状态</th><th>最低称号</th><th>单服规则</th><th>最终结果</th><th class="actions-column">操作</th></tr></thead><tbody><tr v-for="item in accessPreview.servers" :key="item.serverId"><td><div class="meta-stack"><strong>{{ item.serverDisplayName }}</strong><span>{{ item.serverId }}</span></div></td><td>{{ item.isVisible ? item.effectiveStatus : '已归档' }}</td><td>{{ tierText(item.minimumTier) }}</td><td>{{ item.rule ? (item.rule.decision === 'Allow' ? '单服允许' : '单服拒绝') : '无单服规则' }}</td><td><span class="status-badge" :class="item.allowed ? 'status-online' : 'status-closed'">{{ item.allowed ? '允许' : '拒绝' }}</span><small>{{ accessReasonText(item.reason) }}</small></td><td class="actions-column"><button class="icon-button" type="button" title="编辑单服规则" aria-label="编辑单服规则" @click="openRule(item)"><AppIcon name="pencil" /></button></td></tr></tbody></table></div></section>
    <p v-else-if="previewLoading" class="loading-inline" role="status">正在计算最终权限…</p>

    <dialog ref="ruleDialog" class="drawer vue-drawer" @cancel.prevent="ruleDialog?.close()"><form v-if="editingRule" @submit.prevent="saveRule"><header class="drawer-header"><div><span>{{ accessPreview?.user.displayName }} · {{ editingRule.serverDisplayName }}</span><h2>{{ editingRule.rule ? '编辑单服权限规则' : '新增单服权限规则' }}</h2></div><button class="icon-button" type="button" aria-label="关闭" @click="ruleDialog?.close()"><AppIcon name="x" /></button></header><div class="drawer-body"><div v-if="ruleError" class="inline-alert" role="alert"><AppIcon name="circle-alert" /><span>{{ ruleError }}</span></div><label>决定<select v-model="ruleForm.decision"><option value="Allow">单服允许</option><option value="Deny">单服拒绝</option></select></label><label>原因<textarea v-model="ruleForm.reason" maxlength="240" rows="4"></textarea></label><label>到期时间<input v-model="ruleForm.expiresAt" type="datetime-local"><span>留空表示长期有效。</span></label><div v-if="editingRule.rule" class="rule-delete-preview"><strong>删除后的结果</strong><span>{{ predictedAfterDelete(editingRule) }}</span></div></div><footer class="drawer-footer"><button v-if="editingRule.rule" class="button button-danger" type="button" @click="pendingDelete = editingRule">删除规则</button><div><button class="button button-secondary" type="button" @click="ruleDialog?.close()">取消</button><button class="button button-primary" type="submit" :disabled="ruleBusy">{{ ruleBusy ? '保存中…' : '保存规则' }}</button></div></footer></form></dialog>

    <dialog ref="securityDrawer" class="drawer user-security-drawer" @cancel.prevent="closeSecurity"><div v-if="security"><header class="drawer-header"><div><span>{{ tierText(security.user.accessTier) }} · @{{ security.user.username }}</span><h2>{{ security.user.displayName }} 的安全状态</h2></div><button class="icon-button" type="button" aria-label="关闭" @click="closeSecurity"><AppIcon name="x" /></button></header><div class="drawer-body security-drawer-body"><div v-if="securityError" class="inline-alert" role="alert"><AppIcon name="circle-alert" /><span>{{ securityError }}</span></div>
      <section class="security-section"><div class="security-section-heading"><div><span>赫朝账号</span><strong>{{ security.user.displayName }}</strong></div><span class="status-badge" :class="security.user.isDisabled ? 'status-closed' : 'status-online'">{{ security.user.isDisabled ? '已停用' : '正常' }}</span></div><p>@{{ security.user.username }} · {{ security.user.email || '未登记邮箱' }}</p><button class="button security-full-button" :class="security.user.isDisabled ? 'button-secondary' : 'button-danger'" type="button" @click="prepareSecurity(security.user.isDisabled ? 'account-enable' : 'account-disable')">{{ security.user.isDisabled ? '恢复账号' : '停用账号' }}</button></section>
      <section class="security-section"><div class="security-section-heading"><div><span>全局称号</span><strong>{{ tierText(security.user.accessTier) }} · {{ security.user.luckPermsPrimaryGroup }}</strong></div><span class="status-badge" :class="security.pendingLuckPermsTierChange ? 'status-maintenance' : 'status-online'">{{ security.pendingLuckPermsTierChange ? '等待执行' : '已同步' }}</span></div><template v-if="!security.pendingLuckPermsTierChange"><div class="security-tier-fields"><label>目标称号<select v-model="targetTier"><option value="Member">成员</option><option value="Participant">活动成员</option><option value="Collaborator">协作者</option><option value="Administrator">管理员</option></select></label><label>变更原因<input v-model="tierReason" maxlength="500"></label></div><button class="button button-secondary security-full-button" type="button" :disabled="securityBusy || !security.user.minecraftUuid" @click="submitTierChange">提交等级变更</button></template><p v-else>目标 {{ tierText(security.pendingLuckPermsTierChange.targetAccessTier) }} · 已尝试 {{ security.pendingLuckPermsTierChange.attemptCount }} 次</p></section>
      <section class="security-section"><div class="security-section-heading"><div><span>Minecraft 身份</span><strong>{{ security.user.minecraftName || '尚未绑定' }}</strong></div><span class="status-badge" :class="security.minecraftIdentityBan ? 'status-closed' : security.user.minecraftUuid ? 'status-online' : 'status-archived'">{{ security.minecraftIdentityBan ? 'UUID 已封禁' : security.user.minecraftUuid ? '正常' : '未绑定' }}</span></div><p class="security-monospace">{{ security.user.minecraftUuid || '无 Minecraft UUID' }}</p><p v-if="security.minecraftIdentityBan">原因：{{ security.minecraftIdentityBan.reason }} · {{ security.minecraftIdentityBan.expiresAt ? `到期 ${formatDateTime(security.minecraftIdentityBan.expiresAt)}` : '长期封禁' }}</p><button class="button security-full-button" :class="security.minecraftIdentityBan ? 'button-secondary' : 'button-danger'" type="button" :disabled="!security.user.minecraftUuid" @click="prepareSecurity(security.minecraftIdentityBan ? 'minecraft-unban' : 'minecraft-ban')">{{ security.minecraftIdentityBan ? '解除 UUID 封禁' : '封禁 UUID' }}</button></section>
      <section class="security-section"><div class="security-section-heading"><div><span>启动器设备</span><strong>{{ security.launcherSessions.length }} 个活跃会话</strong></div><button class="button button-secondary" type="button" @click="prepareSecurity('sessions-revoke-all')">全部撤销</button></div><div class="security-session-list"><div v-for="item in security.launcherSessions" :key="item.sessionId" class="security-session-item"><div><strong>设备会话 · {{ item.sessionId.slice(-8) }}</strong><span>{{ item.sourceIp || '无来源地址' }} · 最后活动 {{ formatDateTime(item.lastSeenAt) }}</span></div><button class="icon-button" type="button" aria-label="撤销设备会话" @click="prepareSecurity('session-revoke', item.sessionId)"><AppIcon name="log-out" /></button></div><p v-if="!security.launcherSessions.length">当前没有活跃的启动器设备会话。</p></div></section>
      <section class="security-section"><div class="security-section-heading"><div><span>其他即时凭据</span><strong>执行“全部撤销”时一并失效</strong></div></div><dl class="security-count-grid"><div><dt>后台会话</dt><dd>{{ security.activeAdminSessions }}</dd></div><div><dt>登录票据</dt><dd>{{ security.pendingAdminTickets }}</dd></div><div><dt>进服授权</dt><dd>{{ security.pendingVelocityLaunchGrants }}</dd></div><div><dt>论坛撤销待投递</dt><dd>{{ security.pendingForumSessionRevocations }}</dd></div></dl></section>
    </div><footer class="drawer-footer"><span>所有安全操作都会记录原因、操作者和来源地址。</span><button class="button button-secondary" type="button" @click="closeSecurity">关闭</button></footer></div></dialog>

    <dialog ref="securityActionDialog" class="confirm-dialog security-action-dialog" @cancel.prevent="closeSecurityAction"><form v-if="pendingSecurity" @submit.prevent="executeSecurityAction"><div class="confirm-icon"><AppIcon name="key-round" /></div><h2>{{ pendingSecurity.title }}</h2><p>{{ pendingSecurity.message }}</p><div v-if="securityError" class="inline-alert" role="alert"><AppIcon name="circle-alert" /><span>{{ securityError }}</span></div><label class="security-action-field">操作原因<textarea v-model="securityActionReason" minlength="4" maxlength="500" rows="4" required></textarea></label><label v-if="pendingSecurity.expiry" class="security-action-field">封禁到期时间<input v-model="securityActionExpiry" type="datetime-local"><span>留空表示长期封禁。</span></label><div class="confirm-actions"><button class="button button-secondary" type="button" :disabled="securityBusy" @click="closeSecurityAction">取消</button><button class="button" :class="pendingSecurity.danger ? 'button-danger' : 'button-primary'" type="submit" :disabled="securityBusy">{{ pendingSecurity.label }}</button></div></form></dialog>

    <ConfirmDialog :open="Boolean(pendingDelete)" title="删除单服权限规则" :message="pendingDelete ? `将删除 ${accessPreview?.user.displayName} 在 ${pendingDelete.serverDisplayName} 的${pendingDelete.rule?.decision === 'Deny' ? '拒绝' : '允许'}规则。${predictedAfterDelete(pendingDelete)}` : ''" confirm-label="确认删除规则" danger :busy="deleteBusy" @close="pendingDelete = null" @confirm="deleteRule" />
  </section>
</template>
