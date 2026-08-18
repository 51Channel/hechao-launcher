import { defineComponent } from "vue";
import { flushPromises, mount } from "@vue/test-utils";
import { afterEach, describe, expect, it, vi } from "vitest";
import * as apiClient from "@/api/client";
import type { AdminEconomyItemHistory, AdminEconomyOverview } from "@/api/types";
import EconomyView from "@/views/EconomyView.vue";

vi.mock("vue-echarts", () => ({
  default: defineComponent({
    name: "VChart",
    template: '<div class="chart-test-double"></div>'
  })
}));

const populated: AdminEconomyOverview = {
  from: "2026-08-17T05:00:00Z",
  to: "2026-08-18T04:30:00Z",
  hours: 24,
  serverId: null,
  servers: [{ serverId: "survival2", displayName: "天域生存服" }],
  items: [
    { itemId: "minecraft:iron_ingot", currentUnitPrice: 5, enabled: true },
    { itemId: "minecraft:gold_ingot", currentUnitPrice: 8, enabled: false }
  ],
  summary: {
    totalSupply: 12500,
    windowIssued: 850,
    transferVolume: 420,
    activePlayers: 18,
    operationCount: 42
  },
  wealth: {
    fundedAccounts: 36,
    averageBalance: 347.22,
    medianBalance: 185,
    p90Balance: 920,
    topTenPercentShare: 0.41
  },
  series: [
    { at: "2026-08-18T03:00:00Z", totalSupply: 12000, issuedAmount: 350 },
    { at: "2026-08-18T04:00:00Z", totalSupply: 12500, issuedAmount: 500 }
  ],
  topBalances: [{
    playerUuid: "11111111-1111-1111-1111-111111111111",
    playerName: "HechaoPlayer",
    balance: 2400,
    supplyShare: 0.192
  }],
  products: [{ itemId: "minecraft:iron_ingot", quantity: 64, amount: 320, sellers: 4 }],
  serverVolumes: [{
    serverId: "survival2",
    displayName: "天域生存服",
    saleVolume: 850,
    transferVolume: 420,
    activePlayers: 18,
    operationCount: 42
  }]
};

const itemHistory: AdminEconomyItemHistory = {
  from: populated.from,
  to: populated.to,
  hours: 24,
  serverId: null,
  itemId: "minecraft:iron_ingot",
  currentUnitPrice: 5,
  enabled: true,
  summary: {
    openUnitPrice: 4.5,
    closeUnitPrice: 5,
    lowUnitPrice: 4,
    highUnitPrice: 5.5,
    priceChangeRate: 1 / 9,
    quantity: 64,
    amount: 320,
    sellers: 4,
    transactions: 6
  },
  series: [
    { at: "2026-08-18T03:00:00Z", openUnitPrice: 4, closeUnitPrice: 5, averageUnitPrice: 4.5, lowUnitPrice: 4, highUnitPrice: 5, quantity: 24, amount: 108, sellers: 2, transactions: 2 },
    { at: "2026-08-18T04:00:00Z", openUnitPrice: 5.5, closeUnitPrice: 5, averageUnitPrice: 5.3, lowUnitPrice: 5, highUnitPrice: 5.5, quantity: 40, amount: 212, sellers: 3, transactions: 4 }
  ]
};

const empty: AdminEconomyOverview = {
  ...populated,
  summary: { totalSupply: 0, windowIssued: 0, transferVolume: 0, activePlayers: 0, operationCount: 0 },
  wealth: { fundedAccounts: 0, averageBalance: 0, medianBalance: 0, p90Balance: 0, topTenPercentShare: 0 },
  series: populated.series.map(point => ({ ...point, totalSupply: 0, issuedAmount: 0 })),
  topBalances: [],
  items: [],
  products: [],
  serverVolumes: []
};

afterEach(() => {
  vi.restoreAllMocks();
});

describe("EconomyView", () => {
  it("reloads real data when the time range or server changes", async () => {
    const api = vi.spyOn(apiClient, "api").mockImplementation(path => Promise.resolve(
      (path.includes("/items/history") ? itemHistory : populated) as never
    ));
    const wrapper = mount(EconomyView);
    await flushPromises();

    expect(wrapper.text()).toContain("12,500.00");
    await wrapper.findAll(".segmented-control button")[1].trigger("click");
    await flushPromises();
    expect(api.mock.calls.some(call => call[0].includes("/items/history") && call[0].includes("hours=168"))).toBe(true);

    await wrapper.get("select[aria-label='服务器范围']").setValue("survival2");
    await flushPromises();
    expect(api.mock.calls.some(call => call[0].includes("/items/history") && call[0].includes("serverId=survival2"))).toBe(true);
    wrapper.unmount();
  });

  it("loads a selected catalog item and exposes its official buyback metrics", async () => {
    const api = vi.spyOn(apiClient, "api").mockImplementation(path => Promise.resolve(
      (path.includes("/items/history")
        ? { ...itemHistory, itemId: path.includes("gold_ingot") ? "minecraft:gold_ingot" : itemHistory.itemId }
        : populated) as never
    ));
    const wrapper = mount(EconomyView);
    await flushPromises();

    expect(wrapper.text()).toContain("单品官方回收行情");
    expect(wrapper.text()).toContain("+11.11%");
    await wrapper.get("input[aria-label='搜索物品 ID']").setValue("minecraft:gold_ingot");
    await wrapper.get(".economy-item-picker").trigger("submit");
    await flushPromises();

    expect(api.mock.calls.some(call => call[0].includes("itemId=minecraft%3Agold_ingot"))).toBe(true);
    expect(wrapper.text()).toContain("minecraft:gold_ingot");
    wrapper.unmount();
  });

  it("keeps a truthful no-trade state for a configured item", async () => {
    vi.spyOn(apiClient, "api").mockImplementation(path => Promise.resolve(
      (path.includes("/items/history")
        ? {
            ...itemHistory,
            summary: { ...itemHistory.summary, openUnitPrice: null, closeUnitPrice: null, lowUnitPrice: null, highUnitPrice: null, priceChangeRate: null, quantity: 0, amount: 0, sellers: 0, transactions: 0 },
            series: itemHistory.series.map(point => ({ ...point, openUnitPrice: null, closeUnitPrice: null, averageUnitPrice: null, lowUnitPrice: null, highUnitPrice: null, quantity: 0, amount: 0, sellers: 0, transactions: 0 }))
          }
        : populated) as never
    ));
    const wrapper = mount(EconomyView);
    await flushPromises();

    expect(wrapper.text()).toContain("当前范围没有该物品的回收成交");
    expect(wrapper.findAll(".chart-test-double")).toHaveLength(1);
    wrapper.unmount();
  });

  it("keeps item history failures local and recoverable", async () => {
    vi.spyOn(apiClient, "api").mockImplementation(path => path.includes("/items/history")
      ? Promise.reject(new Error("单品聚合查询超时"))
      : Promise.resolve(populated as never));
    const wrapper = mount(EconomyView);
    await flushPromises();

    expect(wrapper.text()).toContain("单品行情暂时不可用");
    expect(wrapper.text()).toContain("单品聚合查询超时");
    expect(wrapper.text()).toContain("12,500.00");
    wrapper.unmount();
  });

  it("shows a truthful empty state instead of fabricated market data", async () => {
    vi.spyOn(apiClient, "api").mockResolvedValue(empty as never);
    const wrapper = mount(EconomyView);
    await flushPromises();

    expect(wrapper.text()).toContain("经济系统暂无交易数据");
    expect(wrapper.find(".chart-test-double").exists()).toBe(false);
    wrapper.unmount();
  });

  it("keeps API failures recoverable", async () => {
    vi.spyOn(apiClient, "api").mockRejectedValue(new Error("经济聚合查询超时"));
    const wrapper = mount(EconomyView);
    await flushPromises();

    expect(wrapper.text()).toContain("数据暂时不可用");
    expect(wrapper.text()).toContain("经济聚合查询超时");
    expect(wrapper.findAll("button").some(button => button.text().includes("重新加载"))).toBe(true);
    wrapper.unmount();
  });
});
