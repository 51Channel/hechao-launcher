package world.hechao.economyscreen.client;

final class BackdropCover {
    private BackdropCover() {
    }

    static Crop calculate(
            int screenWidth,
            int screenHeight,
            int textureWidth,
            int textureHeight) {
        if (screenWidth <= 0
                || screenHeight <= 0
                || textureWidth <= 0
                || textureHeight <= 0) {
            throw new IllegalArgumentException("screen and texture dimensions must be positive");
        }
        double scale = Math.max(
                screenWidth / (double) textureWidth,
                screenHeight / (double) textureHeight);
        int sourceWidth = Math.min(
                textureWidth,
                Math.max(1, (int) Math.round(screenWidth / scale)));
        int sourceHeight = Math.min(
                textureHeight,
                Math.max(1, (int) Math.round(screenHeight / scale)));
        return new Crop(
                (textureWidth - sourceWidth) / 2,
                (textureHeight - sourceHeight) / 2,
                sourceWidth,
                sourceHeight);
    }

    record Crop(
            int sourceX,
            int sourceY,
            int sourceWidth,
            int sourceHeight) {
    }
}
