package world.hechao.economy;

import java.net.URI;
import java.time.Duration;

public record EconomyConfigurationView(
        URI apiBaseUri,
        String serverId,
        String token,
        Duration requestTimeout,
        boolean configured) {

    static EconomyConfigurationView from(EconomyConfiguration configuration) {
        return new EconomyConfigurationView(
                configuration.apiBaseUri(),
                configuration.serverId(),
                configuration.token(),
                configuration.requestTimeout(),
                configuration.isConfigured());
    }
}
