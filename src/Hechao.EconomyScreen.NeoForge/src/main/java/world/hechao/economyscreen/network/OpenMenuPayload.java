package world.hechao.economyscreen.network;

import io.netty.buffer.ByteBuf;
import java.util.ArrayList;
import java.util.List;
import java.util.UUID;
import net.minecraft.network.codec.StreamCodec;
import net.minecraft.network.protocol.common.custom.CustomPacketPayload;
import net.minecraft.resources.ResourceLocation;

public record OpenMenuPayload(
        UUID sessionId,
        List<String> actionIds) implements CustomPacketPayload {
    public static final Type<OpenMenuPayload> TYPE = new Type<>(
            ResourceLocation.fromNamespaceAndPath(
                    "hechao_economy_screen",
                    "open_menu"));

    public static final StreamCodec<ByteBuf, OpenMenuPayload> STREAM_CODEC =
            new StreamCodec<>() {
                @Override
                public OpenMenuPayload decode(ByteBuf buffer) {
                    var sessionId = new UUID(buffer.readLong(), buffer.readLong());
                    int count = readVarInt(buffer);
                    if (count < 1 || count > 16) {
                        throw new IllegalArgumentException("menu button count is invalid");
                    }
                    var actionIds = new ArrayList<String>(count);
                    for (int index = 0; index < count; index++) {
                        actionIds.add(readString(buffer, 32));
                    }
                    return new OpenMenuPayload(sessionId, List.copyOf(actionIds));
                }

                @Override
                public void encode(ByteBuf buffer, OpenMenuPayload payload) {
                    buffer.writeLong(payload.sessionId.getMostSignificantBits());
                    buffer.writeLong(payload.sessionId.getLeastSignificantBits());
                    writeVarInt(buffer, payload.actionIds.size());
                    for (var actionId : payload.actionIds) {
                        writeString(buffer, actionId, 32);
                    }
                }
            };

    public OpenMenuPayload {
        actionIds = List.copyOf(actionIds);
        if (actionIds.isEmpty()
                || actionIds.size() > 16
                || actionIds.stream().distinct().count() != actionIds.size()) {
            throw new IllegalArgumentException("menu actions are invalid");
        }
    }

    @Override
    public Type<? extends CustomPacketPayload> type() {
        return TYPE;
    }

    static String readString(ByteBuf buffer, int maximumCharacters) {
        int length = readVarInt(buffer);
        if (length < 0 || length > maximumCharacters * 4) {
            throw new IllegalArgumentException("payload string is too large");
        }
        var bytes = new byte[length];
        buffer.readBytes(bytes);
        var value = new String(bytes, java.nio.charset.StandardCharsets.UTF_8);
        if (value.length() > maximumCharacters) {
            throw new IllegalArgumentException("payload string is too large");
        }
        return value;
    }

    static void writeString(ByteBuf buffer, String value, int maximumCharacters) {
        if (value.length() > maximumCharacters) {
            throw new IllegalArgumentException("payload string is too large");
        }
        var bytes = value.getBytes(java.nio.charset.StandardCharsets.UTF_8);
        writeVarInt(buffer, bytes.length);
        buffer.writeBytes(bytes);
    }

    static int readVarInt(ByteBuf buffer) {
        int value = 0;
        int position = 0;
        byte current;
        do {
            current = buffer.readByte();
            value |= (current & 0x7F) << position;
            if (position >= 28) {
                throw new IllegalArgumentException("VarInt is too large");
            }
            position += 7;
        } while ((current & 0x80) != 0);
        return value;
    }

    static void writeVarInt(ByteBuf buffer, int value) {
        while ((value & -128) != 0) {
            buffer.writeByte(value & 127 | 128);
            value >>>= 7;
        }
        buffer.writeByte(value);
    }
}
