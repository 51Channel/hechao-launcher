package world.hechao.economyscreen;

import java.util.Locale;
import java.util.Optional;

final class RtpCommandPlan {
    static final int MAXIMUM_RANGE = 5_000;
    static final int BORDER_MARGIN = 32;
    static final int MINIMUM_RANGE = 64;

    private RtpCommandPlan() {
    }

    static Optional<Plan> create(
            double centerX,
            double centerZ,
            double borderSize) {
        if (!Double.isFinite(centerX)
                || !Double.isFinite(centerZ)
                || !Double.isFinite(borderSize)) {
            return Optional.empty();
        }
        int maximumRange = (int) Math.floor(Math.min(
                MAXIMUM_RANGE,
                borderSize / 2.0 - BORDER_MARGIN));
        if (maximumRange < MINIMUM_RANGE) {
            return Optional.empty();
        }
        return Optional.of(new Plan(
                String.format(
                        Locale.ROOT,
                        "minecraft:spreadplayers %.2f %.2f 0 %d false @s",
                        centerX,
                        centerZ,
                        maximumRange),
                maximumRange));
    }

    record Plan(String command, int maximumRange) {
    }
}
