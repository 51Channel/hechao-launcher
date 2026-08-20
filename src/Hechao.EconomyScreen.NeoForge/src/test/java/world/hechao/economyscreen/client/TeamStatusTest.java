package world.hechao.economyscreen.client;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.util.List;
import org.junit.jupiter.api.Test;

final class TeamStatusTest {
    @Test
    void menuSessionFailureStopsTheLoadingState() {
        var status = new TeamStatus();

        status.accept("[赫朝经济] 菜单已失效，请重新打开。");

        assertTrue(status.error());
        assertFalse(status.waiting());
        assertEquals("菜单已失效，请重新打开。", status.feedback());
    }

    @Test
    void parsesCurrentProductionMemberListShape() {
        var status = new TeamStatus();

        status.accept("队伍成员: [队长] 51Channel, Alice, Bob");

        assertEquals(TeamStatus.Membership.JOINED, status.membership());
        assertEquals("51Channel", status.leader());
        assertEquals(List.of("51Channel", "Alice", "Bob"), status.members());
        assertFalse(status.waiting());
    }

    @Test
    void parsesNoTeamResponseAndKnownCommandErrors() {
        var status = new TeamStatus();

        status.accept("你当前不在队伍中。");

        assertEquals(TeamStatus.Membership.NONE, status.membership());
        assertTrue(status.accepts("找不到在线玩家。"));
        assertFalse(status.accepts("Alice 加入了游戏"));
    }

    @Test
    void localConfirmationDoesNotEnterNetworkWaitingState() {
        var status = new TeamStatus();

        status.note("再次点击确认");

        assertFalse(status.waiting());
        assertEquals("再次点击确认", status.feedback());
    }

    @Test
    void parsesSeparateLeaderAndMemberLabels() {
        var status = new TeamStatus();

        status.accept("队长：Captain，成员：Alice、Bob");

        assertEquals("Captain", status.leader());
        assertEquals(List.of("Captain", "Alice", "Bob"), status.members());
    }
}
