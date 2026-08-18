package world.hechao.economy.gui;

import static org.junit.jupiter.api.Assertions.assertEquals;

import org.junit.jupiter.api.Test;
import world.hechao.economy.api.EconomyGatewayException;

final class SellMenuTest {
    @Test
    void translatesMissingProductInsteadOfLeakingHttpStatus() {
        assertEquals(
                "该物品未加入服务器回收目录。",
                SellMenu.quoteError(new EconomyGatewayException(
                        "economy service returned HTTP 404",
                        false,
                        404)));
    }

    @Test
    void translatesUnknownOutcomeWithoutPromisingARefund() {
        assertEquals(
                "经济服务暂时无法响应，请稍后再试。",
                SellMenu.quoteError(new EconomyGatewayException(
                        "response lost",
                        true,
                        0)));
    }
}
