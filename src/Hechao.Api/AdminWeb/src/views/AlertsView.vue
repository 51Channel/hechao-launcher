<script setup lang="ts">
import { computed, onScopeDispose, ref } from "vue";
import { api } from "@/api/client";
import type { AlertRecord, AlertSummary } from "@/api/types";
import { registerPageRefresh } from "@/composables/usePageRefresh";
import { usePolling } from "@/composables/usePolling";
import { useResource } from "@/composables/useResource";
import { showToast } from "@/composables/useToast";
import { formatDateTime, formatRelativeTime } from "@/utils";
import PageHeading from "@/components/PageHeading.vue";
import ResourceState from "@/components/ResourceState.vue";

const resource = useResource(signal => api<AlertSummary>("/v1/admin/operational-alerts", { signal }));
const busy = ref("");
const unregister = registerPageRefresh(resource.refresh); onScopeDispose(unregister); usePolling(resource.refresh, 10_000);
const alerts = computed(() => resource.data.value?.alerts ?? []);
const sourceText = (source: string) => ({ Api: "启动器 API", Authentication: "账号认证", Distribution: "内容分发", Server: "游戏服务", Certificate: "HTTPS 证书", Infrastructure: "基础设施" }[source] ?? source);
const severityText = (value: string) => ({ Critical: "严重", Warning: "警告", Info: "提示" }[value] ?? value);
async function acknowledge(item: AlertRecord): Promise<void> {
  busy.value = item.fingerprint;
  try { await api(`/v1/admin/operational-alerts/${encodeURIComponent(item.fingerprint)}/acknowledge`, { method: "POST" }); showToast("告警已确认；异常恢复前仍保持活动状态"); await resource.refresh(); }
  catch (reason) { showToast(reason instanceof Error ? reason.message : "确认失败。", true); }
  finally { busy.value = ""; }
}
</script>

<template>
  <section class="view-section">
    <PageHeading title="统一告警中心" description="归并 API、认证、下载、服务器、OSS 和证书异常；确认不会掩盖仍在持续的故障。" :updated-at="resource.lastUpdatedAt.value" :stale="Boolean(resource.error.value)" />
    <div class="summary-strip alert-summary"><div><span>活动告警</span><strong>{{ resource.data.value?.activeCount ?? 0 }}</strong></div><div><span>严重</span><strong>{{ resource.data.value?.criticalCount ?? 0 }}</strong></div><div><span>警告</span><strong>{{ resource.data.value?.warningCount ?? 0 }}</strong></div><div><span>未确认</span><strong>{{ resource.data.value?.unacknowledgedCount ?? 0 }}</strong></div></div>
    <ResourceState :loading="resource.loading.value && !resource.data.value" :error="resource.data.value ? '' : resource.error.value" :empty="alerts.length === 0" empty-title="当前没有告警" empty-message="所有监控源均未报告活动或近期异常。" @retry="resource.refresh">
      <div class="table-frame" tabindex="0" aria-label="可滚动数据表"><table class="alert-table"><thead><tr><th>级别</th><th>告警</th><th>来源</th><th>状态</th><th>打开</th><th>最近观测</th><th>操作</th></tr></thead><tbody><tr v-for="item in alerts" :key="item.fingerprint" :class="{ 'alert-resolved': item.status === 'Resolved' }">
        <td><span class="alert-severity" :class="`alert-severity-${item.severity.toLowerCase()}`">{{ severityText(item.severity) }}</span></td><td><div class="meta-stack"><strong>{{ item.title }}</strong><span>{{ item.summary }}</span></div></td><td><div class="meta-stack"><strong>{{ sourceText(item.source) }}</strong><span>{{ item.code }}</span></div></td>
        <td><span class="status-badge" :class="item.status === 'Resolved' ? 'status-archived' : item.acknowledgedAt ? 'status-maintenance' : 'status-online'">{{ item.status === 'Resolved' ? '已恢复' : item.acknowledgedAt ? '已确认' : '活动' }}</span></td>
        <td><div class="meta-stack"><strong>{{ formatRelativeTime(item.openedAt) }}</strong><span>{{ formatDateTime(item.openedAt) }}</span></div></td><td><div class="meta-stack"><strong>{{ formatRelativeTime(item.lastSeenAt) }}</strong><span>{{ item.observationCount }} 次</span></div></td>
        <td><button v-if="item.status === 'Active' && !item.acknowledgedAt" class="button button-secondary" type="button" :disabled="busy === item.fingerprint" @click="acknowledge(item)">确认</button><span v-else class="count-label">{{ formatDateTime(item.resolvedAt || item.acknowledgedAt) }}</span></td>
      </tr></tbody></table></div>
    </ResourceState>
  </section>
</template>
