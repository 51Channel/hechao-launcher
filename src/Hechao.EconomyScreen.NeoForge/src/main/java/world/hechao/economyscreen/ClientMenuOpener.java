package world.hechao.economyscreen;

import net.neoforged.api.distmarker.Dist;
import net.neoforged.api.distmarker.OnlyIn;
import world.hechao.economyscreen.client.HechaoNavigationScreen;
import world.hechao.economyscreen.network.OpenMenuPayload;

@OnlyIn(Dist.CLIENT)
final class ClientMenuOpener {
    private ClientMenuOpener() {
    }

    static void open(OpenMenuPayload payload) {
        net.minecraft.client.Minecraft.getInstance()
                .setScreen(new HechaoNavigationScreen(payload));
    }
}
