package world.hechao.economyscreen.client;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;

final class EconomyResultStateTest {
    @Test
    void stripsEconomyPrefixFromServerMessages() {
        assertEquals(
                "玩家 的余额: 12.50 金币",
                EconomyResultState.normalize(
                        "[赫朝经济] 玩家 的余额: 12.50 金币"));
    }

    @Test
    void balanceResponseCompletesLoadingState() {
        var state = new EconomyResultState("balance", "正在查询余额...");

        state.accept("[赫朝经济] 玩家 的余额: 12.50 金币");

        assertEquals(EconomyResultState.Tone.SUCCESS, state.tone());
        assertEquals(1, state.messages().size());
        assertFalse(state.canConfirmSale());
    }

    @Test
    void saleQuoteEnablesConfirmation() {
        var state = new EconomyResultState("sell", "正在处理主手物品...");

        state.accept("[赫朝经济] 报价: 8 个 minecraft:iron_ingot = 16.00 金币");

        assertTrue(state.canConfirmSale());
    }

    @Test
    void failuresUseErrorTone() {
        var state = new EconomyResultState("balance", "正在查询余额...");

        state.accept("[赫朝经济] 经济请求被拒绝（HTTP 503）。");

        assertEquals(EconomyResultState.Tone.ERROR, state.tone());
    }

    @Test
    void staleMenuResponseIsActionableError() {
        var state = new EconomyResultState("market", "正在打开玩家市场...");

        state.accept("[赫朝经济] 菜单已失效，请重新打开。");

        assertEquals(EconomyResultState.Tone.ERROR, state.tone());
    }

    @Test
    void teamScreenAcceptsEveryLineOfAnUnprefixedResponse() {
        var state = new EconomyResultState("team", "正在打开队伍...");

        assertTrue(state.acceptsUnprefixedMessage("队伍: 工业远征队"));
        state.accept("队伍: 工业远征队");
        assertTrue(state.acceptsUnprefixedMessage("队长: 51Channel"));
        state.accept("队长: 51Channel");
        assertTrue(state.acceptsUnprefixedMessage("成员: Alice"));
        state.accept("成员: Alice");

        assertEquals(3, state.messages().size());
        assertEquals(EconomyResultState.Tone.SUCCESS, state.tone());
        assertFalse(state.acceptsUnprefixedMessage("Alice 加入了游戏"));
    }

    @Test
    void nonTeamScreensRejectUnprefixedTeamMessages() {
        var state = new EconomyResultState("balance", "正在查询余额...");

        assertFalse(state.acceptsUnprefixedMessage("成员: Alice"));
    }

    @Test
    void loadingStateTimesOut() {
        var state = new EconomyResultState("balance", "正在查询余额...");

        for (int tick = 0; tick < 200; tick++) {
            state.tick();
        }

        assertEquals(EconomyResultState.Tone.ERROR, state.tone());
        assertEquals("请求超时，请稍后重试。", state.messages().getFirst());
    }

    @Test
    void errorStateCanBeResetForRetry() {
        var state = new EconomyResultState("market", "正在打开玩家市场...");
        for (int tick = 0; tick < 200; tick++) {
            state.tick();
        }

        state.begin("正在重新打开玩家市场...");

        assertEquals(EconomyResultState.Tone.LOADING, state.tone());
        assertTrue(state.messages().isEmpty());
    }
}
