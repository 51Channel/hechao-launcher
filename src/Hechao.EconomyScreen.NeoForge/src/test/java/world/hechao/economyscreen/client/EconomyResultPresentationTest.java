package world.hechao.economyscreen.client;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;

final class EconomyResultPresentationTest {
    @Test
    void presentsBalanceAsAccountInstrumentData() {
        var state = new EconomyResultState("balance", "正在查询余额...");
        state.accept("[赫朝经济] 51Channel 的余额: 12.50 金币");

        var view = EconomyResultPresentation.from(state);

        assertEquals(EconomyResultPresentation.Kind.BALANCE, view.kind());
        assertEquals("个人账户", view.label());
        assertEquals("12.50", view.primary());
        assertEquals("金币", view.unit());
        assertEquals("51Channel · 可用余额", view.secondary());
        assertTrue(view.hasMonetaryValue());
    }

    @Test
    void presentsFrozenBalanceAndTotalAssets() {
        var state = new EconomyResultState("balance", "正在同步账户...");
        state.accept("[赫朝经济] 51Channel 的余额: 12.50 金币");
        state.accept("[赫朝经济] 冻结余额: 7.50 金币");

        var view = EconomyResultPresentation.from(state);

        assertEquals("12.50", view.primary());
        assertEquals("51Channel · 可用余额", view.secondary());
        assertEquals("冻结 7.50 金币 · 总资产 20.00 金币", view.detail());
    }

    @Test
    void presentsTeamResultAsDedicatedState() {
        var state = new EconomyResultState("team", "正在打开队伍...");
        state.accept("队长: 51Channel");
        state.accept("成员: Alice");

        var view = EconomyResultPresentation.from(state);

        assertEquals(EconomyResultPresentation.Kind.TEAM, view.kind());
        assertEquals("当前队伍", view.primary());
        assertEquals("成员信息已同步", view.secondary());
        assertTrue(view.detail().contains("成员: Alice"));
    }

    @Test
    void presentsQuoteWithRealItemQuantityAndAmount() {
        var state = new EconomyResultState("sell", "正在处理主手物品...");
        state.accept("[赫朝经济] 报价: 8 个 minecraft:iron_ingot = 16.00 金币");
        state.accept("[赫朝经济] 30 秒内输入 /sell confirm 完成出售。");

        var view = EconomyResultPresentation.from(state);

        assertEquals(EconomyResultPresentation.Kind.QUOTE, view.kind());
        assertEquals("16.00", view.primary());
        assertEquals("8 个 · minecraft:iron_ingot", view.secondary());
        assertEquals("minecraft:iron_ingot", view.itemId());
        assertEquals("30 秒内输入 /sell confirm 完成出售。", view.detail());
    }

    @Test
    void presentsCompletedSaleAsDeposit() {
        var state = new EconomyResultState("sell", "正在确认出售...");
        state.accept("[赫朝经济] 出售成功，获得 16.00 金币。");

        var view = EconomyResultPresentation.from(state);

        assertEquals(EconomyResultPresentation.Kind.SALE_SUCCESS, view.kind());
        assertEquals("本次入账", view.label());
        assertEquals("16.00", view.primary());
        assertEquals("交易已完成", view.secondary());
    }

    @Test
    void preservesServerErrorAsDetail() {
        var state = new EconomyResultState("sell", "正在处理主手物品...");
        state.accept("[赫朝经济] 主手没有可出售物品。");

        var view = EconomyResultPresentation.from(state);

        assertEquals(EconomyResultPresentation.Kind.ERROR, view.kind());
        assertEquals("操作未完成", view.primary());
        assertEquals("主手没有可出售物品。", view.detail());
    }

    @Test
    void loadingPresentationDoesNotInventProgress() {
        var state = new EconomyResultState("balance", "正在查询余额...");

        var view = EconomyResultPresentation.from(state);

        assertEquals(EconomyResultPresentation.Kind.LOADING, view.kind());
        assertEquals("正在查询余额...", view.primary());
        assertEquals("正在等待服务器响应", view.secondary());
    }
}
