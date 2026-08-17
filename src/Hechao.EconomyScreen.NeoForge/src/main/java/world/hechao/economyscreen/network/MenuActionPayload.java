package world.hechao.economyscreen.network;

import io.netty.buffer.ByteBuf;
import java.util.UUID;
import net.minecraft.network.codec.StreamCodec;
import net.minecraft.network.protocol.common.custom.CustomPacketPayload;
import net.minecraft.resources.ResourceLocation;

public record MenuActionPayload(UUID sessionId, String actionId)
        implements CustomPacketPayload {
    public static final Type<MenuActionPayload> TYPE = new Type<>(
            ResourceLocation.fromNamespaceAndPath(
                    "hechao_economy_screen",
                    "menu_action"));

    public static final StreamCodec<ByteBuf, MenuActionPayload> STREAM_CODEC =
            new StreamCodec<>() {
                @Override
                public MenuActionPayload decode(ByteBuf buffer) {
                    return new MenuActionPayload(
                            new UUID(buffer.readLong(), buffer.readLong()),
                            OpenMenuPayload.readString(buffer, 32));
                }

                @Override
                public void encode(ByteBuf buffer, MenuActionPayload payload) {
                    buffer.writeLong(payload.sessionId.getMostSignificantBits());
                    buffer.writeLong(payload.sessionId.getLeastSignificantBits());
                    OpenMenuPayload.writeString(buffer, payload.actionId, 32);
                }
            };

    @Override
    public Type<? extends CustomPacketPayload> type() {
        return TYPE;
    }
}
