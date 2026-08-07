package world.hechao.tieragent;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

import java.util.List;
import java.util.UUID;
import java.util.concurrent.CompletableFuture;
import net.luckperms.api.LuckPerms;
import net.luckperms.api.model.data.DataMutateResult;
import net.luckperms.api.model.data.NodeMap;
import net.luckperms.api.model.group.Group;
import net.luckperms.api.model.group.GroupManager;
import net.luckperms.api.model.user.User;
import net.luckperms.api.model.user.UserManager;
import net.luckperms.api.node.types.InheritanceNode;
import org.junit.jupiter.api.Test;

class LuckPermsTierMutationServiceTest {
    @Test
    void persistsStoredPrimaryGroupBeforeSavingUser() {
        var fixture = fixture(DataMutateResult.SUCCESS);
        when(fixture.user().getPrimaryGroup()).thenReturn("default", "vip");

        var result = fixture.service().apply(command())
                .toCompletableFuture()
                .join();

        assertEquals("Applied", result.outcome());
        assertEquals("vip", result.observedPrimaryGroup());
        verify(fixture.user()).setPrimaryGroup("vip");
        verify(fixture.nodeMap()).add(fixture.targetNode());
        verify(fixture.userManager()).saveUser(fixture.user());
    }

    @Test
    void rejectsCommandWhenStoredPrimaryGroupCannotBeUpdated() {
        var fixture = fixture(DataMutateResult.FAIL);
        when(fixture.user().getPrimaryGroup()).thenReturn("default");

        var result = fixture.service().apply(command())
                .toCompletableFuture()
                .join();

        assertEquals("Failed", result.outcome());
        assertEquals("default", result.observedPrimaryGroup());
        assertEquals("primary_group_update_failed", result.failureCode());
        verify(fixture.nodeMap(), never()).toCollection();
        verify(fixture.userManager(), never()).saveUser(any());
    }

    private static Fixture fixture(DataMutateResult primaryGroupResult) {
        var luckPerms = mock(LuckPerms.class);
        var groupManager = mock(GroupManager.class);
        var userManager = mock(UserManager.class);
        var user = mock(User.class);
        var nodeMap = mock(NodeMap.class);
        var targetNode = mock(InheritanceNode.class);
        var minecraftUuid = command().minecraftUuid();

        when(luckPerms.getGroupManager()).thenReturn(groupManager);
        when(groupManager.getGroup("vip")).thenReturn(mock(Group.class));
        when(luckPerms.getUserManager()).thenReturn(userManager);
        when(userManager.loadUser(minecraftUuid))
                .thenReturn(CompletableFuture.completedFuture(user));
        when(userManager.saveUser(user))
                .thenReturn(CompletableFuture.completedFuture(null));
        when(user.setPrimaryGroup("vip")).thenReturn(primaryGroupResult);
        when(user.data()).thenReturn(nodeMap);
        when(nodeMap.toCollection()).thenReturn(List.of());
        when(nodeMap.add(targetNode))
                .thenReturn(DataMutateResult.SUCCESS);

        return new Fixture(
                new LuckPermsTierMutationService(
                        luckPerms,
                        ignored -> targetNode),
                userManager,
                user,
                nodeMap,
                targetNode);
    }

    private static TierCommand command() {
        return new TierCommand(
                UUID.fromString("11111111-1111-1111-1111-111111111111"),
                UUID.fromString("22222222-2222-2222-2222-222222222222"),
                "default",
                "vip",
                "Participant",
                1);
    }

    private record Fixture(
            LuckPermsTierMutationService service,
            UserManager userManager,
            User user,
            NodeMap nodeMap,
            InheritanceNode targetNode) {
    }
}
