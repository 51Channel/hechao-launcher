package world.hechao.economyscreen;

import static org.junit.jupiter.api.Assertions.assertEquals;

import org.junit.jupiter.api.Test;

final class MenuActionsTest {
    @Test
    void includesServerOwnerProductConfigurationAction() {
        var action = MenuActions.all().get("admin_product");

        assertEquals("服主回收设置", action.label());
        assertEquals("heco product", action.command());
    }
}
