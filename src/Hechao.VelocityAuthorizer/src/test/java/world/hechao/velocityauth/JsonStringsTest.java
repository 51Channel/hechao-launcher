package world.hechao.velocityauth;

import static org.junit.jupiter.api.Assertions.assertEquals;

import org.junit.jupiter.api.Test;

final class JsonStringsTest {
    @Test
    void quotesControlCharactersAndSlashes() {
        assertEquals(
                "\"name\\\\with\\n\\\"quotes\\\"\"",
                JsonStrings.quote("name\\with\n\"quotes\""));
    }
}
