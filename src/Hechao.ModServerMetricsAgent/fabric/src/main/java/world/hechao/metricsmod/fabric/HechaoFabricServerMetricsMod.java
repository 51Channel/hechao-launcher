package world.hechao.metricsmod.fabric;

import java.nio.file.Path;
import java.time.Instant;
import net.fabricmc.api.DedicatedServerModInitializer;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerLifecycleEvents;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import world.hechao.metricsmod.ServerMetricsRuntime;
import world.hechao.metricsmod.TickMetricsRecorder;

public final class HechaoFabricServerMetricsMod
        implements DedicatedServerModInitializer {
    private static final Logger LOGGER = LoggerFactory.getLogger(
            "HechaoServerMetrics");
    private static final Path METRICS_PATH = Path.of(
            "plugins",
            "HechaoServerMetrics",
            "metrics.json");

    private final TickMetricsRecorder recorder = new TickMetricsRecorder();
    private final ServerMetricsRuntime runtime = new ServerMetricsRuntime(
            METRICS_PATH,
            message -> LOGGER.warn("{}", message));

    @Override
    public void onInitializeServer() {
        ServerTickEvents.START_SERVER_TICK.register(
                server -> recorder.onTickStart(System.nanoTime()));
        ServerTickEvents.END_SERVER_TICK.register(server ->
                recorder.onTickEnd(System.nanoTime(), Instant.now())
                        .ifPresent(runtime::publish));
        ServerLifecycleEvents.SERVER_STOPPED.register(server -> runtime.close());
        LOGGER.info(
                "Read-only TPS, MSPT and GC metrics will be written every 5 seconds.");
    }
}
