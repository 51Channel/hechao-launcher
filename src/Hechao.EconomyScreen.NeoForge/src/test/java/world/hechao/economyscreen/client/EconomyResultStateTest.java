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
    void loadingStateTimesOut() {
        var state = new EconomyResultState("balance", "正在查询余额...");

        for (int tick = 0; tick < 200; tick++) {
            state.tick();
        }

        assertEquals(EconomyResultState.Tone.ERROR, state.tone());
        assertEquals("请求超时，请稍后重试。", state.messages().getFirst());
    }
}
