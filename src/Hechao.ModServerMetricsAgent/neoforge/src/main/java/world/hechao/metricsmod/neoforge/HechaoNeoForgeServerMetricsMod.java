package world.hechao.metricsmod.neoforge;

import com.mojang.logging.LogUtils;
import java.nio.file.Path;
import java.time.Instant;
import net.neoforged.fml.common.Mod;
import net.neoforged.neoforge.common.NeoForge;
import net.neoforged.neoforge.event.server.ServerStoppedEvent;
import net.neoforged.neoforge.event.tick.ServerTickEvent;
import org.slf4j.Logger;
import world.hechao.metricsmod.ServerMetricsRuntime;
import world.hechao.metricsmod.TickMetricsRecorder;

@Mod(HechaoNeoForgeServerMetricsMod.MOD_ID)
public final class HechaoNeoForgeServerMetricsMod {
    public static final String MOD_ID = "hechao_server_metrics";

    private static final Logger LOGGER = LogUtils.getLogger();
    private static final Path METRICS_PATH = Path.of(
            "plugins",
            "HechaoServerMetrics",
            "metrics.json");

    private final TickMetricsRecorder recorder = new TickMetricsRecorder();
    private final ServerMetricsRuntime runtime = new ServerMetricsRuntime(
            METRICS_PATH,
            message -> LOGGER.warn("{}", message));

    public HechaoNeoForgeServerMetricsMod() {
        NeoForge.EVENT_BUS.addListener(this::onTickStart);
        NeoForge.EVENT_BUS.addListener(this::onTickEnd);
        NeoForge.EVENT_BUS.addListener(this::onServerStopped);
        LOGGER.info(
                "Read-only TPS, MSPT and GC metrics will be written every 5 seconds.");
    }

    private void onTickStart(ServerTickEvent.Pre event) {
        recorder.onTickStart(System.nanoTime());
    }

    private void onTickEnd(ServerTickEvent.Post event) {
        recorder.onTickEnd(System.nanoTime(), Instant.now())
                .ifPresent(runtime::publish);
    }

    private void onServerStopped(ServerStoppedEvent event) {
        runtime.close();
    }
}
