<script setup lang="ts">
import { computed, onScopeDispose } from "vue";
import { api } from "@/api/client";
import type { RuntimeSummary, RuntimeTarget } from "@/api/types";
import { registerPageRefresh } from "@/composables/usePageRefresh";
import { usePolling } from "@/composables/usePolling";
import { useResource } from "@/composables/useResource";
import { formatBytes, formatRelativeTime } from "@/utils";
import PageHeading from "@/components/PageHeading.vue";
import ResourceState from "@/components/ResourceState.vue";

const resource = useResource(signal => api<RuntimeSummary>("/v1/admin/server-runtime/summary", { signal }));
const unregister = registerPageRefresh(resource.refresh); onScopeDispose(unregister); usePolling(resource.refresh, 10_000);
const summary = computed(() => ({
  fresh: resource.data.value?.targets.filter(x => x.isFresh).length ?? 0,
  online: resource.data.value?.targets.filter(x => x.isFresh && x.online).length ?? 0,
  players: resource.data.value?.targets.filter(x => x.isFresh).reduce((sum, x) => sum + x.onlinePlayers, 0) ?? 0,
  issues: resource.data.value?.targets.filter(x => x.issues.length > 0).length ?? 0
}));
const status = (item: RuntimeTarget) => !item.hasHeartbeat ? ["未上报", "status-archived"] : !item.isFresh ? ["已过期", "status-maintenance"] : item.online ? ["运行中", "status-online"] : ["离线", "status-archived"];
const tick = (item: RuntimeTarget) => item.tps1m == null ? "—" : `${item.tps1m.toFixed(1)} TPS · ${item.msptAverage?.toFixed(1) ?? '—'} MSPT`;
const issueText = (issue: string) => ({ StatusTimeout: "状态查询超时", StatusUnavailable: "状态不可用", ProcessProbeNotConfigured: "未配置进程探针", ProcessNotRunning: "进程未运行", ProcessAccessDenied: "进程访问被拒绝", ProcessProbeFailed: "进程探测失败", DiskProbeFailed: "磁盘探测失败", MetricsNotConfigured: "未配置性能指标", MetricsFileMissing: "性能指标文件缺失", MetricsFileStale: "性能指标已过期", MetricsFileInvalid: "性能指标无效" }[issue] ?? issue);
</script>

<template>
  <section class="view-section">
    <PageHeading title="服务状态" description="来自两台 VPS 的只出站采集器；心跳过期时明确标记，不把旧数据当作实时状态。" :updated-at="resource.lastUpdatedAt.value" :stale="Boolean(resource.error.value)" />
    <div class="summary-strip runtime-summary"><div><span>新鲜目标</span><strong>{{ summary.fresh }}</strong></div><div><span>在线目标</span><strong>{{ summary.online }}</strong></div><div><span>在线玩家</span><strong>{{ summary.players }}</strong></div><div><span>异常目标</span><strong>{{ summary.issues }}</strong></div></div>
    <ResourceState :loading="resource.loading.value && !resource.data.value" :error="resource.data.value ? '' : resource.error.value" :empty="resource.data.value?.targets.length === 0" empty-title="暂无服务器心跳" @retry="resource.refresh">
      <div v-if="resource.data.value" class="table-frame" tabindex="0" aria-label="可滚动数据表"><table class="runtime-table"><thead><tr><th>目标</th><th>状态</th><th>TPS / MSPT</th><th>进程</th><th>磁盘</th><th>问题</th></tr></thead><tbody><tr v-for="item in resource.data.value.targets" :key="item.velocityTarget">
        <td><div class="meta-stack"><strong>{{ item.servers.map(x => x.displayName).join('、') || item.velocityTarget }}</strong><span>{{ item.velocityTarget }} · {{ item.collectorInstance || '—' }}</span></div></td>
        <td><span class="status-badge" :class="status(item)[1]">{{ status(item)[0] }}</span><small>{{ formatRelativeTime(item.receivedAt) }}</small></td>
        <td><div class="meta-stack"><strong>{{ tick(item) }}</strong><span v-if="item.tps5m != null">5m {{ item.tps5m.toFixed(1) }} · 15m {{ item.tps15m?.toFixed(1) }}</span></div></td>
        <td><div class="meta-stack"><strong>{{ formatBytes(item.processWorkingSetBytes) }}</strong><span>{{ item.processCpuPercent?.toFixed(1) ?? '—' }}% CPU</span></div></td>
        <td><div class="meta-stack"><strong>{{ formatBytes(item.diskFreeBytes) }} 可用</strong><span>共 {{ formatBytes(item.diskTotalBytes) }}</span></div></td>
        <td><span v-if="!item.issues.length">正常</span><div v-else class="issue-list"><span v-for="issue in item.issues" :key="issue">{{ issueText(issue) }}</span></div></td>
      </tr></tbody></table></div>
    </ResourceState>
  </section>
</template>
