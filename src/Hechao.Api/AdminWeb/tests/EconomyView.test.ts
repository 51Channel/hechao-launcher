import { defineComponent } from "vue";
import { flushPromises, mount } from "@vue/test-utils";
import { afterEach, describe, expect, it, vi } from "vitest";
import * as apiClient from "@/api/client";
import type { AdminEconomyOverview } from "@/api/types";
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

const empty: AdminEconomyOverview = {
  ...populated,
  summary: { totalSupply: 0, windowIssued: 0, transferVolume: 0, activePlayers: 0, operationCount: 0 },
  wealth: { fundedAccounts: 0, averageBalance: 0, medianBalance: 0, p90Balance: 0, topTenPercentShare: 0 },
  series: populated.series.map(point => ({ ...point, totalSupply: 0, issuedAmount: 0 })),
  topBalances: [],
  products: [],
  serverVolumes: []
};

afterEach(() => {
  vi.restoreAllMocks();
});

describe("EconomyView", () => {
  it("reloads real data when the time range or server changes", async () => {
    const api = vi.spyOn(apiClient, "api").mockResolvedValue(populated as never);
    const wrapper = mount(EconomyView);
    await flushPromises();

    expect(wrapper.text()).toContain("12,500.00");
    await wrapper.findAll(".segmented-control button")[1].trigger("click");
    await flushPromises();
    expect(api.mock.calls.at(-1)?.[0]).toContain("hours=168");

    await wrapper.get("select[aria-label='服务器范围']").setValue("survival2");
    await flushPromises();
    expect(api.mock.calls.at(-1)?.[0]).toContain("serverId=survival2");
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
