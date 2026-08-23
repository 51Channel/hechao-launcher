package world.hechao.economyscreen;

final class RtpSafetyPolicy {
    private RtpSafetyPolicy() {
    }

    static boolean accepts(Surface surface) {
        return surface.insideBorder()
                && surface.supportSolid()
                && surface.feetClear()
                && surface.headClear()
                && surface.fluidsClear()
                && surface.hazardsClear()
                && surface.collisionFree();
    }

    record Surface(
            boolean insideBorder,
            boolean supportSolid,
            boolean feetClear,
            boolean headClear,
            boolean fluidsClear,
            boolean hazardsClear,
            boolean collisionFree) {
    }
}
