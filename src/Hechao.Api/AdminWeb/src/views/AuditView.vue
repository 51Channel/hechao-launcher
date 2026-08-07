<script setup lang="ts">
import { computed, nextTick, onScopeDispose, ref } from "vue";
import { api } from "@/api/client";
import type { AuditEntry } from "@/api/types";
import { registerPageRefresh } from "@/composables/usePageRefresh";
import { showToast } from "@/composables/useToast";
import { formatDateTime } from "@/utils";
import AppIcon from "@/components/AppIcon.vue";
import PageHeading from "@/components/PageHeading.vue";
import ResourceState from "@/components/ResourceState.vue";

const entries = ref<AuditEntry[]>([]); const loading = ref(false); const error = ref(""); const beforeId = ref<number | null>(null); const hasMore = ref(true); const updatedAt = ref<Date | null>(null);
const search = ref(""); const category = ref("all"); const selected = ref<AuditEntry | null>(null); const dialog = ref<HTMLDialogElement | null>(null);
const labels: Record<string, string> = {
  "catalog.server.created":"新增服务器","catalog.server.updated":"编辑服务器","catalog.server.archived":"归档服务器","catalog.server.restored":"恢复服务器",
  "catalog.client_profile.created":"创建客户端档案","catalog.client_profile.updated":"编辑客户端档案","catalog.client_profile.enabled":"启用客户端档案","catalog.client_profile.disabled":"停用客户端档案","catalog.client_profile.archived":"归档客户端档案","catalog.client_profile.restored":"恢复客户端档案","catalog.client_profile.deleted":"永久删除客户端档案",
  "catalog.client_profile_release.imported":"导入签名客户端版本","catalog.client_profile_release.hydrated":"补全迁移版本元数据","catalog.client_profile_release.paused":"暂停客户端版本","catalog.client_profile_release.resumed":"恢复客户端版本",
  "catalog.client_profile_channel.updated":"更新客户端发布通道","catalog.client_profile_channel.rolled_back":"回滚客户端发布通道",
  "access.server_rule.created":"新增单服权限规则","access.server_rule.updated":"编辑单服权限规则","access.server_rule.deleted":"清除单服权限规则",
  "security.account.disabled":"停用赫朝账号","security.account.enabled":"恢复赫朝账号","security.sessions.revoked_all":"撤销全部账号会话","security.session.revoked":"撤销设备会话","security.minecraft_ban.created":"封禁 Minecraft UUID","security.minecraft_ban.updated":"更新 Minecraft UUID 封禁","security.minecraft_ban.revoked":"解除 Minecraft UUID 封禁",
  "luckperms.tier_change.queued":"称号变更已排队","luckperms.tier_change.completed":"称号变更已完成",
  "server_control.operation.queued":"服控操作已排队","server_control.operation.completed":"服控操作已完成",
  "velocity.launch_grant.created":"创建进服授权","velocity.launch_grant.consumed":"进服授权已使用","velocity.authorization.denied":"拒绝代理连接",
  "operational_alert.acknowledged":"确认运维告警","admin.login_ticket.created":"创建后台登录票据","admin.web_session.created":"登录管理后台","admin.web_session.revoked":"退出管理后台","admin.mfa.enrollment.started":"开始设置双重验证","admin.mfa.enabled":"启用双重验证","admin.mfa.verified":"完成双重验证","admin.mfa.recovery_code_used":"使用恢复码","admin.trusted_device.created":"信任管理设备","admin.trusted_device.used":"受信任设备免动态码","admin.trusted_device.revoked":"取消受信任设备",
  "diagnostic.upload.authorized":"授权诊断上传","diagnostic.upload.completed":"诊断包上传完成","diagnostic.upload.failed":"诊断包上传失败","diagnostic.upload.expired":"诊断包到期删除","diagnostic.admin.downloaded":"管理员下载诊断包"
};

const filtered = computed(() => entries.value.filter(item => {
  if (category.value !== "all" && !item.action.startsWith(category.value)) return false;
  const query = search.value.trim().toLocaleLowerCase("zh-CN");
  return !query || [labels[item.action] ?? item.action, item.action, item.targetType, item.targetId, item.actorDisplayName ?? "", item.sourceIp ?? ""].some(value => value.toLocaleLowerCase("zh-CN").includes(query));
}));

async function load(reset = false): Promise<void> {
  if (loading.value) return; loading.value = true; error.value = "";
  try {
    const cursor = reset ? null : beforeId.value;
    const batch = await api<AuditEntry[]>(`/v1/admin/audit-logs?limit=50${cursor ? `&beforeId=${cursor}` : ""}`);
    entries.value = reset ? batch : [...entries.value, ...batch]; beforeId.value = batch.at(-1)?.id ?? beforeId.value; hasMore.value = batch.length === 50; updatedAt.value = new Date();
  } catch (reason) { error.value = reason instanceof Error ? reason.message : "审计记录加载失败。"; }
  finally { loading.value = false; }
}
const unregister = registerPageRefresh(() => load(true)); onScopeDispose(unregister); void load(true);
async function openDetail(item: AuditEntry): Promise<void> { selected.value = item; await nextTick(); dialog.value?.showModal(); }
function closeDetail(): void { dialog.value?.close(); selected.value = null; }
function pretty(value: unknown): string { return value == null ? "无" : JSON.stringify(value, null, 2); }
function changedKeys(item: AuditEntry): string[] {
  const before = item.beforeData && typeof item.beforeData === "object" ? item.beforeData as Record<string, unknown> : {};
  const after = item.afterData && typeof item.afterData === "object" ? item.afterData as Record<string, unknown> : {};
  return [...new Set([...Object.keys(before), ...Object.keys(after)])].filter(key => JSON.stringify(before[key]) !== JSON.stringify(after[key]));
}
async function copyId(): Promise<void> {
  if (!selected.value) return;
  try {
    await navigator.clipboard.writeText(String(selected.value.id));
    showToast("审计编号已复制");
  } catch {
    showToast("无法访问剪贴板，请手动记录审计编号。", true);
  }
}
</script>

<template>
  <section class="view-section">
    <PageHeading title="审计记录" description="所有管理、权限、发布、服控与身份变更均按时间倒序保存。" :updated-at="updatedAt" :stale="Boolean(error)">
      <template #actions><button class="button button-secondary" type="button" :disabled="loading || !hasMore" @click="load(false)"><AppIcon name="clock" />{{ hasMore ? '更早记录' : '已加载全部' }}</button></template>
    </PageHeading>
    <div v-if="error && entries.length" class="stale-banner" role="status"><AppIcon name="circle-alert" />刷新失败，当前展示上次成功读取的记录。<button type="button" @click="load(true)">重试</button></div>
    <div class="toolbar audit-toolbar"><label class="search-control"><AppIcon name="search" /><input v-model="search" type="search" placeholder="搜索动作、对象、操作者或来源"></label><select v-model="category" aria-label="审计分类"><option value="all">全部分类</option><option value="catalog.">目录与发布</option><option value="access.">访问规则</option><option value="security.">账号安全</option><option value="server_control.">服控</option><option value="luckperms.">称号</option><option value="admin.">管理员身份</option><option value="operational_alert.">运维告警</option><option value="diagnostic.">诊断包</option><option value="velocity.">进服授权</option></select></div>
    <ResourceState :loading="loading && !entries.length" :error="entries.length ? '' : error" :empty="filtered.length === 0" empty-title="没有符合条件的审计记录" @retry="load(true)">
      <div class="audit-list"><button v-for="item in filtered" :key="item.id" class="audit-entry audit-entry-button" type="button" @click="openDetail(item)"><div class="audit-icon"><AppIcon name="scroll-text" /></div><div class="audit-meta audit-main"><strong>{{ labels[item.action] ?? item.action }}</strong><span>{{ item.targetType }} · {{ item.targetId }}</span></div><div class="audit-meta"><strong>{{ item.actorDisplayName || '系统' }}</strong><span>{{ item.sourceIp || '无来源地址' }}</span></div><div class="audit-meta"><strong>{{ formatDateTime(item.createdAt) }}</strong><span>记录 #{{ item.id }}</span></div></button></div>
    </ResourceState>
    <dialog ref="dialog" class="drawer audit-detail-drawer" @cancel.prevent="closeDetail"><div v-if="selected"><header class="drawer-header"><div><span>审计记录 #{{ selected.id }}</span><h2>{{ labels[selected.action] ?? selected.action }}</h2></div><button class="icon-button" type="button" aria-label="关闭" @click="closeDetail"><AppIcon name="x" /></button></header><div class="drawer-body"><dl class="audit-detail-facts"><div><dt>动作代码</dt><dd>{{ selected.action }}</dd></div><div><dt>目标</dt><dd>{{ selected.targetType }} · {{ selected.targetId }}</dd></div><div><dt>操作者</dt><dd>{{ selected.actorDisplayName || '系统' }}</dd></div><div><dt>来源地址</dt><dd>{{ selected.sourceIp || '无' }}</dd></div><div><dt>时间</dt><dd>{{ formatDateTime(selected.createdAt) }}</dd></div><div><dt>变更字段</dt><dd>{{ changedKeys(selected).join('、') || '无字段差异' }}</dd></div></dl><div class="audit-diff-grid"><section><h3>变更前</h3><pre>{{ pretty(selected.beforeData) }}</pre></section><section><h3>变更后</h3><pre>{{ pretty(selected.afterData) }}</pre></section></div></div><footer class="drawer-footer"><span>原始审计数据只读展示</span><div><button class="button button-secondary" type="button" @click="copyId">复制编号</button><button class="button button-primary" type="button" @click="closeDetail">关闭</button></div></footer></div></dialog>
  </section>
</template>
