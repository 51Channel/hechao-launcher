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

    static boolean fitsInsideCheckedAirColumn(
            double minX,
            double minY,
            double minZ,
            double maxX,
            double maxY,
            double maxZ,
            int feetX,
            int feetY,
            int feetZ) {
        return minX >= feetX
                && maxX <= feetX + 1.0
                && minY >= feetY
                && maxY <= feetY + 2.0
                && minZ >= feetZ
                && maxZ <= feetZ + 1.0;
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
