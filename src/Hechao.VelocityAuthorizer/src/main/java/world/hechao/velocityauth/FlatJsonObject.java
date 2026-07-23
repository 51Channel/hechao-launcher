package world.hechao.velocityauth;

import java.util.LinkedHashMap;
import java.util.Map;

final class FlatJsonObject {
    private final Map<String, Object> values;

    private FlatJsonObject(Map<String, Object> values) {
        this.values = values;
    }

    static FlatJsonObject parse(String json) {
        Parser parser = new Parser(json);
        FlatJsonObject result = new FlatJsonObject(parser.parseObject());
        parser.skipWhitespace();
        if (!parser.isAtEnd()) {
            throw new IllegalArgumentException("Unexpected content after JSON object");
        }
        return result;
    }

    boolean requiredBoolean(String name) {
        Object value = values.get(name);
        if (value instanceof Boolean booleanValue) {
            return booleanValue;
        }
        throw new IllegalArgumentException("Missing boolean JSON property: " + name);
    }

    String requiredString(String name) {
        String value = nullableString(name);
        if (value == null) {
            throw new IllegalArgumentException("Missing string JSON property: " + name);
        }
        return value;
    }

    String nullableString(String name) {
        Object value = values.get(name);
        if (value == null || value instanceof String) {
            return (String) value;
        }
        throw new IllegalArgumentException("Invalid string JSON property: " + name);
    }

    private static final class Parser {
        private final String json;
        private int offset;

        private Parser(String json) {
            if (json == null || json.length() > 16_384) {
                throw new IllegalArgumentException("JSON response is empty or too large");
            }
            this.json = json;
        }

        private Map<String, Object> parseObject() {
            skipWhitespace();
            expect('{');
            skipWhitespace();
            Map<String, Object> result = new LinkedHashMap<>();
            if (tryConsume('}')) {
                return result;
            }

            while (true) {
                skipWhitespace();
                String key = parseString();
                skipWhitespace();
                expect(':');
                skipWhitespace();
                result.put(key, parseValue());
                skipWhitespace();
                if (tryConsume('}')) {
                    return result;
                }
                expect(',');
            }
        }

        private Object parseValue() {
            if (peek() == '"') {
                return parseString();
            }
            if (consumeLiteral("true")) {
                return true;
            }
            if (consumeLiteral("false")) {
                return false;
            }
            if (consumeLiteral("null")) {
                return null;
            }
            throw new IllegalArgumentException("Unsupported JSON value at offset " + offset);
        }

        private String parseString() {
            expect('"');
            StringBuilder result = new StringBuilder();
            while (!isAtEnd()) {
                char character = json.charAt(offset++);
                if (character == '"') {
                    return result.toString();
                }
                if (character != '\\') {
                    if (character < 0x20) {
                        throw new IllegalArgumentException("Control character in JSON string");
                    }
                    result.append(character);
                    continue;
                }

                if (isAtEnd()) {
                    throw new IllegalArgumentException("Incomplete JSON escape sequence");
                }
                char escaped = json.charAt(offset++);
                switch (escaped) {
                    case '"', '\\', '/' -> result.append(escaped);
                    case 'b' -> result.append('\b');
                    case 'f' -> result.append('\f');
                    case 'n' -> result.append('\n');
                    case 'r' -> result.append('\r');
                    case 't' -> result.append('\t');
                    case 'u' -> result.append(parseUnicodeEscape());
                    default -> throw new IllegalArgumentException("Invalid JSON escape sequence");
                }
            }
            throw new IllegalArgumentException("Unterminated JSON string");
        }

        private char parseUnicodeEscape() {
            if (offset + 4 > json.length()) {
                throw new IllegalArgumentException("Incomplete Unicode escape sequence");
            }
            int value = 0;
            for (int index = 0; index < 4; index++) {
                int digit = Character.digit(json.charAt(offset++), 16);
                if (digit < 0) {
                    throw new IllegalArgumentException("Invalid Unicode escape sequence");
                }
                value = (value << 4) | digit;
            }
            return (char) value;
        }

        private boolean consumeLiteral(String value) {
            if (!json.regionMatches(offset, value, 0, value.length())) {
                return false;
            }
            offset += value.length();
            return true;
        }

        private void expect(char expected) {
            if (isAtEnd() || json.charAt(offset) != expected) {
                throw new IllegalArgumentException(
                        "Expected '" + expected + "' at offset " + offset);
            }
            offset++;
        }

        private boolean tryConsume(char expected) {
            if (!isAtEnd() && json.charAt(offset) == expected) {
                offset++;
                return true;
            }
            return false;
        }

        private char peek() {
            if (isAtEnd()) {
                throw new IllegalArgumentException("Unexpected end of JSON input");
            }
            return json.charAt(offset);
        }

        private void skipWhitespace() {
            while (!isAtEnd() && Character.isWhitespace(json.charAt(offset))) {
                offset++;
            }
        }

        private boolean isAtEnd() {
            return offset >= json.length();
        }
    }
}
