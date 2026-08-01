<script setup lang="ts">
import { computed, onScopeDispose, ref } from "vue";
import { api } from "@/api/client";
import type { TelemetrySummary } from "@/api/types";
import { registerPageRefresh } from "@/composables/usePageRefresh";
import { useResource } from "@/composables/useResource";
import { formatBytes, formatDateTime, formatPercentage } from "@/utils";
import PageHeading from "@/components/PageHeading.vue";
import ResourceState from "@/components/ResourceState.vue";

const hours = ref(24);
const resource = useResource(signal => api<TelemetrySummary>(`/v1/admin/telemetry/summary?hours=${hours.value}`, { signal }));
const unregister = registerPageRefresh(resource.refresh);
onScopeDispose(unregister);
void resource.refresh();

const period = computed(() => resource.data.value
  ? `${formatDateTime(resource.data.value.from)} 至 ${formatDateTime(resource.data.value.to)}` : "");

async function selectHours(value: number): Promise<void> { hours.value = value; await resource.refresh(); }
const eventText = (type: string) => ({ LauncherStarted: "启动器启动", Install: "安装", Repair: "修复", Rollback: "回滚", Launch: "启动游戏", GameExit: "游戏退出" }[type] ?? type);
const failureText = (code: string) => ({ NetworkUnavailable: "网络不可用", IntegrityFailed: "完整性校验失败", InsufficientDiskSpace: "磁盘空间不足", RuntimePreparationFailed: "Java 准备失败", MicrosoftReauthenticationRequired: "微软登录过期", GameExitedNonZero: "游戏异常退出", Unexpected: "未分类异常" }[code] ?? code);
</script>

<template>
  <section class="view-section">
    <PageHeading title="运行数据" description="观察客户端下载、安装和启动结果，不采集聊天、文件内容或完整异常正文。" :updated-at="resource.lastUpdatedAt.value" :stale="Boolean(resource.error.value)">
      <template #actions><div class="segmented-control" role="group" aria-label="统计时间范围"><button v-for="item in [[24,'24 小时'],[168,'7 天'],[720,'30 天']]" :key="item[0]" type="button" :class="{ active: hours === item[0] }" :aria-pressed="hours === item[0]" @click="selectHours(Number(item[0]))">{{ item[1] }}</button></div></template>
    </PageHeading>
    <ResourceState :loading="resource.loading.value && !resource.data.value" :error="resource.data.value ? '' : resource.error.value" :empty="false" @retry="resource.refresh">
      <template v-if="resource.data.value">
        <p class="period-label">{{ period }}</p>
        <div class="summary-strip telemetry-summary"><div><span>事件数</span><strong>{{ resource.data.value.eventCount.toLocaleString('zh-CN') }}</strong></div><div><span>活跃玩家</span><strong>{{ resource.data.value.uniqueUsers.toLocaleString('zh-CN') }}</strong></div><div><span>下载失败率</span><strong>{{ formatPercentage(resource.data.value.downloads.failureRate) }}</strong></div><div><span>启动失败率</span><strong>{{ formatPercentage(resource.data.value.launches.failureRate) }}</strong></div></div>
        <div class="telemetry-operation-grid">
          <section class="telemetry-operation"><div class="telemetry-operation-heading"><h3>客户端下载</h3><strong>{{ formatBytes(resource.data.value.downloads.bytes) }}</strong></div><dl class="telemetry-facts"><div><dt>尝试</dt><dd>{{ resource.data.value.downloads.attempts }}</dd></div><div><dt>成功</dt><dd>{{ resource.data.value.downloads.succeeded }}</dd></div><div><dt>失败</dt><dd>{{ resource.data.value.downloads.failed }}</dd></div><div><dt>取消</dt><dd>{{ resource.data.value.downloads.canceled }}</dd></div></dl></section>
          <section class="telemetry-operation"><div class="telemetry-operation-heading"><h3>游戏启动</h3><strong>{{ formatPercentage(resource.data.value.launches.failureRate) }} 失败</strong></div><dl class="telemetry-facts"><div><dt>尝试</dt><dd>{{ resource.data.value.launches.attempts }}</dd></div><div><dt>成功</dt><dd>{{ resource.data.value.launches.succeeded }}</dd></div><div><dt>失败</dt><dd>{{ resource.data.value.launches.failed }}</dd></div><div><dt>取消</dt><dd>{{ resource.data.value.launches.canceled }}</dd></div></dl></section>
        </div>
        <div class="telemetry-table-grid">
          <section><h3>启动器版本</h3><div class="table-frame" tabindex="0" aria-label="可滚动数据表"><table><thead><tr><th>版本</th><th>玩家</th></tr></thead><tbody><tr v-for="item in resource.data.value.launcherVersions" :key="item.launcherVersion"><td>{{ item.launcherVersion }}</td><td>{{ item.users }}</td></tr></tbody></table><p v-if="!resource.data.value.launcherVersions.length" class="empty-inline">暂无版本样本</p></div></section>
          <section><h3>客户端版本</h3><div class="table-frame" tabindex="0" aria-label="可滚动数据表"><table><thead><tr><th>档案</th><th>版本</th><th>玩家</th><th>事件</th></tr></thead><tbody><tr v-for="item in resource.data.value.profileVersions" :key="`${item.profileId}:${item.profileVersion}`"><td>{{ item.profileId }}</td><td>{{ item.profileVersion }}</td><td>{{ item.users }}</td><td>{{ item.events }}</td></tr></tbody></table><p v-if="!resource.data.value.profileVersions.length" class="empty-inline">暂无客户端样本</p></div></section>
        </div>
        <section class="telemetry-failure-section"><h3>失败原因</h3><div class="table-frame" tabindex="0" aria-label="可滚动数据表"><table><thead><tr><th>环节</th><th>原因</th><th>次数</th></tr></thead><tbody><tr v-for="item in resource.data.value.failures" :key="`${item.type}:${item.failureCode}`"><td>{{ eventText(item.type) }}</td><td>{{ failureText(item.failureCode) }}</td><td>{{ item.count }}</td></tr></tbody></table><p v-if="!resource.data.value.failures.length" class="empty-inline">当前窗口没有失败事件</p></div></section>
      </template>
    </ResourceState>
  </section>
</template>
