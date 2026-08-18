<script setup lang="ts">
import { computed, onScopeDispose, ref } from "vue";
import { BarChart, CandlestickChart, LineChart } from "echarts/charts";
import {
  AriaComponent,
  DataZoomComponent,
  GridComponent,
  LegendComponent,
  TooltipComponent
} from "echarts/components";
import { use } from "echarts/core";
import { CanvasRenderer } from "echarts/renderers";
import type { EChartsOption } from "echarts";
import VChart from "vue-echarts";
import { api } from "@/api/client";
import type { AdminEconomyItemHistory, AdminEconomyOverview } from "@/api/types";
import { registerPageRefresh } from "@/composables/usePageRefresh";
import { useResource } from "@/composables/useResource";
import { formatDateTime, formatPercentage } from "@/utils";
import PageHeading from "@/components/PageHeading.vue";
import ResourceState from "@/components/ResourceState.vue";

use([
  CanvasRenderer,
  LineChart,
  BarChart,
  CandlestickChart,
  GridComponent,
  TooltipComponent,
  LegendComponent,
  DataZoomComponent,
  AriaComponent
]);

const windows = [[24, "24 小时"], [168, "7 天"], [720, "30 天"], [2160, "90 天"]] as const;
const hours = ref(24);
const serverId = ref("");
const selectedItemId = ref("");
const itemInput = ref("");
const itemSelectionError = ref("");
const resource = useResource(signal => {
  const query = new URLSearchParams({ hours: String(hours.value) });
  if (serverId.value) query.set("serverId", serverId.value);
  return api<AdminEconomyOverview>(`/v1/admin/economy/overview?${query}`, { signal });
});
const itemResource = useResource(signal => {
  const query = new URLSearchParams({
    hours: String(hours.value),
    itemId: selectedItemId.value
  });
  if (serverId.value) query.set("serverId", serverId.value);
  return api<AdminEconomyItemHistory>(`/v1/admin/economy/items/history?${query}`, { signal });
});
const unregister = registerPageRefresh(refreshDashboard);
onScopeDispose(unregister);
void refreshDashboard();

const hasData = computed(() => Boolean(
  resource.data.value && (
    resource.data.value.summary.totalSupply > 0 ||
    resource.data.value.summary.operationCount > 0 ||
    resource.data.value.topBalances.length > 0 ||
    resource.data.value.items.length > 0
  )
));
const selectedServerName = computed(() => {
  if (!serverId.value) return "全部服务器";
  return resource.data.value?.servers.find(item => item.serverId === serverId.value)?.displayName ?? serverId.value;
});
const period = computed(() => resource.data.value
  ? `${formatDateTime(resource.data.value.from)} 至 ${formatDateTime(resource.data.value.to)}`
  : "");

const money = new Intl.NumberFormat("zh-CN", {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2
});
const compactMoney = new Intl.NumberFormat("zh-CN", {
  notation: "compact",
  maximumFractionDigits: 1
});
const priceChange = new Intl.NumberFormat("zh-CN", {
  style: "percent",
  minimumFractionDigits: 0,
  maximumFractionDigits: 2
});
const formatMoney = (value: number) => money.format(value);
const formatOptionalMoney = (value: number | null) => value === null ? "--" : formatMoney(value);
const formatChange = (value: number | null) => {
  if (value === null) return "--";
  const formatted = priceChange.format(Math.abs(value));
  return `${value > 0 ? "+" : value < 0 ? "-" : ""}${formatted}`;
};
const playerLabel = (name: string | null, uuid: string) => name || `未绑定 · ${uuid.slice(0, 8)}`;
const itemHasTrades = computed(() => (itemResource.data.value?.summary.transactions ?? 0) > 0);

const chartOption = computed<EChartsOption>(() => {
  const data = resource.data.value;
  if (!data) return {} as EChartsOption;
  return {
    animationDuration: 260,
    animationEasing: "cubicOut",
    aria: { enabled: true, description: "全局货币总量与所选服务器新增货币趋势" },
    color: ["#b4231d", "#16794a"],
    grid: { left: 18, right: 24, top: 54, bottom: data.hours >= 720 ? 64 : 38, containLabel: true },
    legend: {
      top: 12,
      left: 16,
      itemWidth: 18,
      itemHeight: 8,
      textStyle: { color: "#515760", fontSize: 12 }
    },
    tooltip: {
      trigger: "axis",
      backgroundColor: "#15171a",
      borderWidth: 0,
      padding: [10, 12],
      textStyle: { color: "#ffffff", fontSize: 12 },
      valueFormatter: value => formatMoney(Number(value))
    },
    xAxis: {
      type: "time",
      boundaryGap: [0, 0],
      axisLine: { lineStyle: { color: "#c9cdd3" } },
      axisTick: { show: false },
      axisLabel: { color: "#626a74", fontSize: 11, hideOverlap: true },
      splitLine: { show: false }
    },
    yAxis: [
      {
        type: "value",
        name: "货币总量",
        nameTextStyle: { color: "#626a74", align: "left" },
        axisLabel: { color: "#626a74", formatter: value => compactMoney.format(Number(value)) },
        splitLine: { lineStyle: { color: "#eceef1" } }
      },
      {
        type: "value",
        name: "新增",
        nameTextStyle: { color: "#626a74", align: "right" },
        axisLabel: { color: "#626a74", formatter: value => compactMoney.format(Number(value)) },
        splitLine: { show: false }
      }
    ],
    dataZoom: data.hours >= 720 ? [{ type: "inside", xAxisIndex: 0 }, { type: "slider", height: 18, bottom: 8 }] : [],
    series: [
      {
        name: "全局货币总量",
        type: "line",
        yAxisIndex: 0,
        showSymbol: false,
        smooth: 0.18,
        lineStyle: { width: 2.5 },
        areaStyle: { color: "rgba(180, 35, 29, 0.08)" },
        data: data.series.map(point => [point.at, point.totalSupply])
      },
      {
        name: `${selectedServerName.value}新增`,
        type: "bar",
        yAxisIndex: 1,
        barMaxWidth: 18,
        itemStyle: { borderRadius: [2, 2, 0, 0] },
        data: data.series.map(point => [point.at, point.issuedAmount])
      }
    ]
  };
});

const itemChartOption = computed<EChartsOption>(() => {
  const data = itemResource.data.value;
  if (!data) return {} as EChartsOption;
  const labels = new Intl.DateTimeFormat("zh-CN", data.hours === 24
    ? { hour: "2-digit", minute: "2-digit", hour12: false }
    : { month: "2-digit", day: "2-digit" });
  return {
    animationDuration: 260,
    animationEasing: "cubicOut",
    aria: { enabled: true, description: `${data.itemId} 官方回收价格开盘、收盘、最低和最高 K 线` },
    grid: { left: 18, right: 24, top: 28, bottom: data.hours >= 720 ? 64 : 38, containLabel: true },
    tooltip: {
      trigger: "axis",
      axisPointer: { type: "cross" },
      backgroundColor: "#15171a",
      borderWidth: 0,
      padding: [10, 12],
      textStyle: { color: "#ffffff", fontSize: 12 }
    },
    xAxis: {
      type: "category",
      data: data.series.map(point => labels.format(new Date(point.at))),
      boundaryGap: true,
      axisLine: { lineStyle: { color: "#c9cdd3" } },
      axisTick: { show: false },
      axisLabel: { color: "#626a74", fontSize: 11, hideOverlap: true },
      splitLine: { show: false }
    },
    yAxis: {
      type: "value",
      name: "官方回收价",
      scale: true,
      nameTextStyle: { color: "#626a74", align: "left" },
      axisLabel: { color: "#626a74", formatter: value => formatMoney(Number(value)) },
      splitLine: { lineStyle: { color: "#eceef1" } }
    },
    dataZoom: data.hours >= 720 ? [{ type: "inside", xAxisIndex: 0 }, { type: "slider", height: 18, bottom: 8 }] : [],
    series: [
      {
        name: "官方回收价（开 / 收 / 低 / 高）",
        type: "candlestick",
        itemStyle: {
          color: "#c9272c",
          color0: "#16845b",
          borderColor: "#c9272c",
          borderColor0: "#16845b",
          borderColorDoji: "#626a74"
        },
        data: data.series.map(point => point.openUnitPrice === null || point.closeUnitPrice === null || point.lowUnitPrice === null || point.highUnitPrice === null
          ? ["-", "-", "-", "-"]
          : [point.openUnitPrice, point.closeUnitPrice, point.lowUnitPrice, point.highUnitPrice])
      }
    ]
  };
});

function ensureSelectedItem(data: AdminEconomyOverview): void {
  if (selectedItemId.value && data.items.some(item => item.itemId === selectedItemId.value)) return;
  const preferred = data.products.find(product => data.items.some(item => item.itemId === product.itemId))?.itemId
    ?? data.items.find(item => item.enabled)?.itemId
    ?? data.items[0]?.itemId
    ?? "";
  selectedItemId.value = preferred;
  itemInput.value = preferred;
}

async function refreshDashboard(): Promise<void> {
  const data = await resource.refresh();
  if (!data) return;
  ensureSelectedItem(data);
  if (selectedItemId.value) await itemResource.refresh();
}

async function viewItem(itemId: string): Promise<void> {
  const normalized = itemId.trim();
  if (!resource.data.value?.items.some(item => item.itemId === normalized)) {
    itemSelectionError.value = "请选择商品目录中存在的物品 ID。";
    return;
  }

  itemSelectionError.value = "";
  selectedItemId.value = normalized;
  itemInput.value = normalized;
  await itemResource.refresh();
}

async function selectItem(): Promise<void> {
  await viewItem(itemInput.value);
}

async function selectHours(value: number): Promise<void> {
  if (hours.value === value) return;
  hours.value = value;
  await refreshDashboard();
}

async function selectServer(): Promise<void> {
  await refreshDashboard();
}
</script>

<template>
  <section class="view-section economy-view">
    <PageHeading
      title="经济监控"
      description="查看全局货币供给、交易流量与财富分布。服务器筛选只影响交易数据，全局货币总量始终跨服统计。"
      :updated-at="resource.lastUpdatedAt.value"
      :stale="Boolean(resource.error.value)"
    >
      <template #actions>
        <div class="economy-filters">
          <label class="economy-server-filter">
            <span class="sr-only">服务器范围</span>
            <select v-model="serverId" aria-label="服务器范围" @change="selectServer">
              <option value="">全部服务器</option>
              <option v-for="server in resource.data.value?.servers ?? []" :key="server.serverId" :value="server.serverId">
                {{ server.displayName }}
              </option>
            </select>
          </label>
          <div class="segmented-control" role="group" aria-label="统计时间范围">
            <button
              v-for="item in windows"
              :key="item[0]"
              type="button"
              :class="{ active: hours === item[0] }"
              :aria-pressed="hours === item[0]"
              @click="selectHours(item[0])"
            >{{ item[1] }}</button>
          </div>
        </div>
      </template>
    </PageHeading>

    <ResourceState
      :loading="resource.loading.value && !resource.data.value"
      :error="resource.data.value ? '' : resource.error.value"
      :empty="Boolean(resource.data.value) && !hasData"
      empty-title="经济系统暂无交易数据"
      empty-message="第一笔出售或转账完成后，这里会开始形成真实行情，不会生成演示数据。"
      @retry="resource.refresh"
    >
      <template v-if="resource.data.value && hasData">
        <p class="economy-period">{{ period }} · 流量范围：{{ selectedServerName }}</p>
        <div class="summary-strip economy-summary">
          <div><span>全局货币总量</span><strong>{{ formatMoney(resource.data.value.summary.totalSupply) }}</strong></div>
          <div><span>区间新增货币</span><strong>{{ formatMoney(resource.data.value.summary.windowIssued) }}</strong></div>
          <div><span>转账成交额</span><strong>{{ formatMoney(resource.data.value.summary.transferVolume) }}</strong></div>
          <div><span>区间活跃玩家</span><strong>{{ resource.data.value.summary.activePlayers.toLocaleString('zh-CN') }}</strong></div>
        </div>

        <section class="economy-market" aria-labelledby="economy-market-title">
          <div class="economy-section-heading">
            <div><h2 id="economy-market-title">货币供给行情</h2><p>红线为跨服总量，绿柱为当前筛选范围内的新增货币。</p></div>
            <span>{{ resource.data.value.summary.operationCount.toLocaleString('zh-CN') }} 笔操作</span>
          </div>
          <VChart class="economy-chart" :option="chartOption" autoresize aria-label="货币供给行情图" />
        </section>

        <section class="economy-item-market" aria-labelledby="economy-item-market-title">
          <div class="economy-section-heading economy-item-heading">
            <div>
              <h2 id="economy-item-market-title">单品官方回收行情</h2>
              <p>价格来自已完成的官方回收报价，不代表玩家自由市场；服务器筛选只影响成交数据。</p>
            </div>
            <form class="economy-item-picker" @submit.prevent="selectItem">
              <label>
                <span class="sr-only">搜索物品 ID</span>
                <input v-model="itemInput" list="economy-item-options" aria-label="搜索物品 ID" autocomplete="off" placeholder="minecraft:iron_ingot">
                <datalist id="economy-item-options">
                  <option v-for="item in resource.data.value.items" :key="item.itemId" :value="item.itemId">
                    {{ item.enabled ? '已启用' : '已停用' }} · {{ formatOptionalMoney(item.currentUnitPrice) }}
                  </option>
                </datalist>
              </label>
              <button type="submit" class="secondary-button">查看行情</button>
            </form>
          </div>
          <p v-if="itemSelectionError" class="field-error" role="alert">{{ itemSelectionError }}</p>

          <div v-if="!resource.data.value.items.length" class="economy-item-empty">
            <strong>尚未配置回收物品</strong>
            <span>商品目录出现物品后，这里会提供逐项行情。</span>
          </div>
          <div v-else-if="itemResource.loading.value && !itemResource.data.value" class="economy-item-empty" aria-live="polite">
            <strong>正在读取单品行情</strong>
          </div>
          <div v-else-if="itemResource.error.value && !itemResource.data.value" class="economy-item-empty" role="alert">
            <strong>单品行情暂时不可用</strong>
            <span>{{ itemResource.error.value }}</span>
            <button type="button" class="secondary-button" @click="itemResource.refresh">重新加载</button>
          </div>
          <template v-else-if="itemResource.data.value">
            <div class="economy-item-context">
              <code>{{ itemResource.data.value.itemId }}</code>
              <span :class="['economy-item-status', { disabled: !itemResource.data.value.enabled }]">
                {{ itemResource.data.value.enabled ? '目录已启用' : '目录已停用' }}
              </span>
              <span>{{ selectedServerName }}成交</span>
            </div>
            <dl class="economy-facts economy-item-facts">
              <div><dt>当前目录价</dt><dd>{{ formatOptionalMoney(itemResource.data.value.currentUnitPrice) }}</dd></div>
              <div><dt>窗口首价</dt><dd>{{ formatOptionalMoney(itemResource.data.value.summary.openUnitPrice) }}</dd></div>
              <div><dt>窗口末价</dt><dd>{{ formatOptionalMoney(itemResource.data.value.summary.closeUnitPrice) }}</dd></div>
              <div><dt>最低 / 最高</dt><dd class="economy-price-range">{{ formatOptionalMoney(itemResource.data.value.summary.lowUnitPrice) }} / {{ formatOptionalMoney(itemResource.data.value.summary.highUnitPrice) }}</dd></div>
              <div><dt>区间涨跌</dt><dd :class="{ positive: (itemResource.data.value.summary.priceChangeRate ?? 0) > 0, negative: (itemResource.data.value.summary.priceChangeRate ?? 0) < 0 }">{{ formatChange(itemResource.data.value.summary.priceChangeRate) }}</dd></div>
              <div><dt>成交数量</dt><dd>{{ itemResource.data.value.summary.quantity.toLocaleString('zh-CN') }}</dd></div>
              <div><dt>成交金额</dt><dd>{{ formatMoney(itemResource.data.value.summary.amount) }}</dd></div>
              <div><dt>卖家 / 成交</dt><dd>{{ itemResource.data.value.summary.sellers.toLocaleString('zh-CN') }} / {{ itemResource.data.value.summary.transactions.toLocaleString('zh-CN') }}</dd></div>
            </dl>
            <VChart v-if="itemHasTrades" class="economy-chart economy-item-chart" :option="itemChartOption" autoresize aria-label="单品官方回收行情图" />
            <div v-else class="economy-item-empty">
              <strong>当前范围没有该物品的回收成交</strong>
              <span>目录价格仍可查看；有真实成交后才绘制价格与成交量，不补造空白行情。</span>
            </div>
          </template>
        </section>

        <section class="economy-wealth" aria-labelledby="economy-wealth-title">
          <div class="economy-section-heading">
            <div><h2 id="economy-wealth-title">财富分布</h2><p>基于所有余额大于零的账户计算。</p></div>
          </div>
          <dl class="economy-facts">
            <div><dt>有余额账户</dt><dd>{{ resource.data.value.wealth.fundedAccounts.toLocaleString('zh-CN') }}</dd></div>
            <div><dt>平均余额</dt><dd>{{ formatMoney(resource.data.value.wealth.averageBalance) }}</dd></div>
            <div><dt>中位余额</dt><dd>{{ formatMoney(resource.data.value.wealth.medianBalance) }}</dd></div>
            <div><dt>P90 余额</dt><dd>{{ formatMoney(resource.data.value.wealth.p90Balance) }}</dd></div>
            <div><dt>前 10% 占比</dt><dd>{{ formatPercentage(resource.data.value.wealth.topTenPercentShare) }}</dd></div>
          </dl>
        </section>

        <div class="economy-table-grid">
          <section>
            <div class="economy-section-heading"><div><h2>玩家余额</h2><p>当前全局余额最高的 10 个账户。</p></div></div>
            <div class="table-frame" tabindex="0" aria-label="玩家余额排行表">
              <table class="economy-table"><thead><tr><th>玩家</th><th>余额</th><th>占总量</th></tr></thead><tbody>
                <tr v-for="player in resource.data.value.topBalances" :key="player.playerUuid">
                  <td><strong>{{ playerLabel(player.playerName, player.playerUuid) }}</strong></td>
                  <td>{{ formatMoney(player.balance) }}</td><td>{{ formatPercentage(player.supplyShare) }}</td>
                </tr>
              </tbody></table>
              <p v-if="!resource.data.value.topBalances.length" class="empty-inline">暂无有余额账户</p>
            </div>
          </section>
          <section>
            <div class="economy-section-heading"><div><h2>物资回收</h2><p>当前窗口内按回收金额排序。</p></div></div>
            <div class="table-frame" tabindex="0" aria-label="物资回收排行表">
              <table class="economy-table"><thead><tr><th>物品</th><th>数量</th><th>回收额</th><th>卖家</th></tr></thead><tbody>
                <tr v-for="product in resource.data.value.products" :key="product.itemId">
                  <td><button type="button" class="economy-item-link" @click="viewItem(product.itemId)">{{ product.itemId }}</button></td><td>{{ product.quantity.toLocaleString('zh-CN') }}</td>
                  <td>{{ formatMoney(product.amount) }}</td><td>{{ product.sellers.toLocaleString('zh-CN') }}</td>
                </tr>
              </tbody></table>
              <p v-if="!resource.data.value.products.length" class="empty-inline">当前窗口没有物资回收成交</p>
            </div>
          </section>
        </div>

        <section class="economy-servers" aria-labelledby="economy-servers-title">
          <div class="economy-section-heading"><div><h2 id="economy-servers-title">服务器交易流量</h2><p>对比各服在当前窗口内产生的经济活动。</p></div></div>
          <div class="table-frame" tabindex="0" aria-label="服务器交易流量表">
            <table class="economy-server-table"><thead><tr><th>服务器</th><th>出售额</th><th>转账额</th><th>活跃玩家</th><th>操作数</th></tr></thead><tbody>
              <tr v-for="server in resource.data.value.serverVolumes" :key="server.serverId">
                <td><strong>{{ server.displayName }}</strong><small>{{ server.serverId }}</small></td>
                <td>{{ formatMoney(server.saleVolume) }}</td><td>{{ formatMoney(server.transferVolume) }}</td>
                <td>{{ server.activePlayers.toLocaleString('zh-CN') }}</td><td>{{ server.operationCount.toLocaleString('zh-CN') }}</td>
              </tr>
            </tbody></table>
            <p v-if="!resource.data.value.serverVolumes.length" class="empty-inline">当前窗口没有服务器交易</p>
          </div>
        </section>
      </template>
    </ResourceState>
  </section>
</template>
