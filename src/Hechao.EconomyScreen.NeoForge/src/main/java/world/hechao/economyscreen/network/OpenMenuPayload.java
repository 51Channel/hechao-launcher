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
        String title,
        String subtitle,
        List<MenuButton> buttons) implements CustomPacketPayload {
    public static final Type<OpenMenuPayload> TYPE = new Type<>(
            ResourceLocation.fromNamespaceAndPath(
                    "hechao_economy_screen",
                    "open_menu"));

    public static final StreamCodec<ByteBuf, OpenMenuPayload> STREAM_CODEC =
            new StreamCodec<>() {
                @Override
                public OpenMenuPayload decode(ByteBuf buffer) {
                    var sessionId = new UUID(buffer.readLong(), buffer.readLong());
                    var title = readString(buffer, 64);
                    var subtitle = readString(buffer, 160);
                    int count = readVarInt(buffer);
                    if (count < 1 || count > 16) {
                        throw new IllegalArgumentException("menu button count is invalid");
                    }
                    var buttons = new ArrayList<MenuButton>(count);
                    for (int index = 0; index < count; index++) {
                        buttons.add(new MenuButton(
                                readString(buffer, 32),
                                readString(buffer, 32),
                                readString(buffer, 96)));
                    }
                    return new OpenMenuPayload(
                            sessionId,
                            title,
                            subtitle,
                            List.copyOf(buttons));
                }

                @Override
                public void encode(ByteBuf buffer, OpenMenuPayload payload) {
                    buffer.writeLong(payload.sessionId.getMostSignificantBits());
                    buffer.writeLong(payload.sessionId.getLeastSignificantBits());
                    writeString(buffer, payload.title, 64);
                    writeString(buffer, payload.subtitle, 160);
                    writeVarInt(buffer, payload.buttons.size());
                    for (var button : payload.buttons) {
                        writeString(buffer, button.actionId, 32);
                        writeString(buffer, button.label, 32);
                        writeString(buffer, button.description, 96);
                    }
                }
            };

    public OpenMenuPayload {
        buttons = List.copyOf(buttons);
    }

    @Override
    public Type<? extends CustomPacketPayload> type() {
        return TYPE;
    }

    public record MenuButton(String actionId, String label, String description) {
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
