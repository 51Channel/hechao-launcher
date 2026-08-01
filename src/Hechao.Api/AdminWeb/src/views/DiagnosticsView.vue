<script setup lang="ts">
import { onScopeDispose, ref } from "vue";
import { api, download } from "@/api/client";
import type { DiagnosticUpload } from "@/api/types";
import { registerPageRefresh } from "@/composables/usePageRefresh";
import { usePolling } from "@/composables/usePolling";
import { useResource } from "@/composables/useResource";
import { showToast } from "@/composables/useToast";
import { formatBytes, formatDateTime, shortHash } from "@/utils";
import AppIcon from "@/components/AppIcon.vue";
import PageHeading from "@/components/PageHeading.vue";
import ResourceState from "@/components/ResourceState.vue";

const resource = useResource(signal => api<DiagnosticUpload[]>("/v1/admin/diagnostics?limit=200", { signal }));
const downloading = ref("");
const unregister = registerPageRefresh(resource.refresh); onScopeDispose(unregister); usePolling(resource.refresh, 30_000);
async function downloadItem(item: DiagnosticUpload): Promise<void> {
  downloading.value = item.uploadId;
  try { await download(`/v1/admin/diagnostics/${encodeURIComponent(item.uploadId)}/download`, `Hechao-Diagnostic-${item.uploadId}.zip`); showToast("诊断包已下载，操作已写入审计记录"); }
  catch (reason) { showToast(reason instanceof Error ? reason.message : "下载失败。", true); }
  finally { downloading.value = ""; }
}
</script>

<template>
  <section class="view-section">
    <PageHeading title="玩家诊断包" description="仅展示玩家主动上传且已脱敏的诊断包，到期后自动删除。" :updated-at="resource.lastUpdatedAt.value" :stale="Boolean(resource.error.value)" />
    <ResourceState :loading="resource.loading.value && !resource.data.value" :error="resource.data.value ? '' : resource.error.value" :empty="resource.data.value?.length === 0" empty-title="暂无待处理诊断包" empty-message="玩家必须先在启动器中生成并明确确认上传。" @retry="resource.refresh">
      <div v-if="resource.data.value" class="table-frame" tabindex="0" aria-label="可滚动数据表"><table class="diagnostic-table"><thead><tr><th>诊断编号</th><th>玩家</th><th>客户端档案</th><th>大小</th><th>上传时间</th><th>自动删除</th><th class="actions-column">操作</th></tr></thead><tbody><tr v-for="item in resource.data.value" :key="item.uploadId"><td><div class="meta-stack"><strong>{{ item.uploadId }}</strong><span>{{ shortHash(item.sha256) }}</span></div></td><td>{{ item.accountDisplayName }}</td><td><div class="meta-stack"><strong>{{ item.profileId }}</strong><span>启动器 {{ item.launcherVersion }}</span></div></td><td>{{ formatBytes(item.size) }}</td><td>{{ formatDateTime(item.uploadedAt) }}</td><td>{{ formatDateTime(item.expiresAt) }}</td><td class="actions-column"><button class="icon-button" type="button" title="下载诊断包" aria-label="下载诊断包" :disabled="downloading === item.uploadId" @click="downloadItem(item)"><AppIcon name="save" /></button></td></tr></tbody></table></div>
    </ResourceState>
  </section>
</template>
