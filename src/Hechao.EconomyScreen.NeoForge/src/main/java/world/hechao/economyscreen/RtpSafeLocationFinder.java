package world.hechao.economyscreen;

import java.util.Optional;
import net.minecraft.core.BlockPos;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.tags.BlockTags;
import net.minecraft.util.RandomSource;
import net.minecraft.world.level.ChunkPos;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.chunk.LevelChunk;
import net.minecraft.world.level.levelgen.Heightmap;
import net.minecraft.world.level.border.WorldBorder;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.shapes.CollisionContext;

final class RtpSafeLocationFinder {
    static final int MAX_ATTEMPTS = 48;
    static final int MAX_VERTICAL_SCAN = 256;

    private RtpSafeLocationFinder() {
    }

    static Optional<Candidate> sample(
            WorldBorder border,
            int maximumRange,
            RandomSource random) {
        int x = sampleCoordinate(border.getCenterX(), maximumRange, random);
        int z = sampleCoordinate(border.getCenterZ(), maximumRange, random);
        if (!insideBorder(border, x + 0.5, z + 0.5)) {
            return Optional.empty();
        }
        return Optional.of(new Candidate(x, z));
    }

    static Optional<Location> findLoaded(
            ServerPlayer player,
            LevelChunk chunk,
            Candidate candidate) {
        ServerLevel level = player.serverLevel();
        WorldBorder border = level.getWorldBorder();
        if (!chunk.getPos().equals(candidate.chunkPos())
                || !insideBorder(
                        border,
                        candidate.x() + 0.5,
                        candidate.z() + 0.5)) {
            return Optional.empty();
        }

        int minBuildHeight = level.getMinBuildHeight();
        int maxBuildHeight = level.getMaxBuildHeight();
        int height = chunk.getHeight(
                        Heightmap.Types.MOTION_BLOCKING_NO_LEAVES,
                        candidate.x(),
                        candidate.z())
                + 1;
        int startY = Math.min(
                maxBuildHeight - 2,
                Math.max(minBuildHeight + 1, height));
        int endY = Math.max(
                minBuildHeight + 1,
                startY - MAX_VERTICAL_SCAN);
        for (int y = startY; y >= endY; y--) {
            BlockPos feet = new BlockPos(candidate.x(), y, candidate.z());
            if (isSafe(level, player, border, chunk, feet)) {
                return Optional.of(new Location(
                        candidate.x() + 0.5,
                        y,
                        candidate.z() + 0.5));
            }
        }
        return Optional.empty();
    }

    private static boolean isSafe(
            ServerLevel level,
            ServerPlayer player,
            WorldBorder border,
            LevelChunk chunk,
            BlockPos feet) {
        BlockState feetState = chunk.getBlockState(feet);
        BlockState headState = chunk.getBlockState(feet.above());
        BlockState supportState = chunk.getBlockState(feet.below());
        boolean supportSolid = !supportState.getCollisionShape(
                level,
                feet.below(),
                CollisionContext.empty()).isEmpty();
        boolean fluidsClear = feetState.getFluidState().isEmpty()
                && headState.getFluidState().isEmpty()
                && supportState.getFluidState().isEmpty();
        boolean hazardsClear = !isHazard(feetState)
                && !isHazard(headState)
                && !isHazard(supportState);
        if (!supportSolid
                || !feetState.isAir()
                || !headState.isAir()
                || !fluidsClear
                || !hazardsClear) {
            return false;
        }

        AABB targetBox = player.getBoundingBox().move(
                feet.getX() + 0.5 - player.getX(),
                feet.getY() - player.getY(),
                feet.getZ() + 0.5 - player.getZ());

        return RtpSafetyPolicy.accepts(new RtpSafetyPolicy.Surface(
                insideBorder(border, feet.getX() + 0.5, feet.getZ() + 0.5),
                supportSolid,
                feetState.isAir(),
                headState.isAir(),
                fluidsClear,
                hazardsClear,
                level.noCollision(player, targetBox)));
    }

    private static boolean isHazard(BlockState state) {
        return state.is(Blocks.BEDROCK)
                || state.is(Blocks.LAVA)
                || state.is(Blocks.MAGMA_BLOCK)
                || state.is(Blocks.FIRE)
                || state.is(Blocks.SOUL_FIRE)
                || state.is(Blocks.CAMPFIRE)
                || state.is(Blocks.SOUL_CAMPFIRE)
                || state.is(Blocks.CACTUS)
                || state.is(Blocks.SWEET_BERRY_BUSH)
                || state.is(Blocks.POWDER_SNOW)
                || state.is(BlockTags.FIRE);
    }

    private static boolean insideBorder(
            WorldBorder border,
            double x,
            double z) {
        double halfSize = Math.max(0.0, border.getSize() / 2.0 - 1.0);
        return x >= border.getCenterX() - halfSize
                && x <= border.getCenterX() + halfSize
                && z >= border.getCenterZ() - halfSize
                && z <= border.getCenterZ() + halfSize;
    }

    private static int sampleCoordinate(
            double center,
            int maximumRange,
            RandomSource random) {
        double value = center
                + (random.nextDouble() * 2.0 - 1.0) * maximumRange;
        return (int) Math.floor(value);
    }

    record Location(double x, double y, double z) {
    }

    record Candidate(int x, int z) {
        ChunkPos chunkPos() {
            return new ChunkPos(x >> 4, z >> 4);
        }
    }
}
