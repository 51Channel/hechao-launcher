ALTER TABLE launcher.servers
    ADD COLUMN allow_protocol_translation boolean NOT NULL DEFAULT false;

COMMENT ON COLUMN launcher.servers.allow_protocol_translation IS
    'Allow clients from a different Minecraft protocol version when a tested proxy translator is active.';
