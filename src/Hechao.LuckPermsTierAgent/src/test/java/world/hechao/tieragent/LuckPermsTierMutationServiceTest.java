package world.hechao.tieragent;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.inOrder;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.verifyNoInteractions;
import static org.mockito.Mockito.when;

import java.util.List;
import java.util.Optional;
import java.util.UUID;
import java.util.concurrent.CompletableFuture;
import net.luckperms.api.LuckPerms;
import net.luckperms.api.messaging.MessagingService;
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
    void persistsAndBroadcastsTierChange() {
        var fixture = fixture("default", false, true);

        var result = fixture.service().apply(command())
                .toCompletableFuture()
                .join();

        assertEquals("Applied", result.outcome());
        assertEquals("vip", result.observedPrimaryGroup());
        var order = inOrder(
                fixture.nodeMap(),
                fixture.user(),
                fixture.userManager(),
                fixture.messagingService());
        order.verify(fixture.userManager())
                .loadUser(command().minecraftUuid());
        order.verify(fixture.nodeMap()).add(fixture.targetNode());
        order.verify(fixture.nodeMap()).remove(fixture.defaultNode());
        order.verify(fixture.user()).setPrimaryGroup("vip");
        order.verify(fixture.userManager()).saveUser(fixture.user());
        order.verify(fixture.messagingService()).pushUserUpdate(fixture.user());
        verify(fixture.userManager(), never()).getUser(any(UUID.class));
    }

    @Test
    void repairsStoredGroupWhenCalculatedGroupAlreadyMatchesTarget() {
        var fixture = fixture("vip", true, true);

        var result = fixture.service().apply(command())
                .toCompletableFuture()
                .join();

        assertEquals("Applied", result.outcome());
        verify(fixture.user()).setPrimaryGroup("vip");
        verify(fixture.nodeMap()).remove(fixture.defaultNode());
        verify(fixture.userManager()).saveUser(fixture.user());
        verify(fixture.messagingService()).pushUserUpdate(fixture.user());
    }

    @Test
    void rejectsCommandWhenStoredPrimaryGroupCannotBeUpdated() {
        var fixture = fixture("default", false, true);
        when(fixture.user().setPrimaryGroup("vip"))
                .thenReturn(DataMutateResult.FAIL);

        var result = fixture.service().apply(command())
                .toCompletableFuture()
                .join();

        assertEquals("Failed", result.outcome());
        assertEquals("primary_group_update_failed", result.failureCode());
        verify(fixture.nodeMap()).add(fixture.defaultNode());
        verify(fixture.nodeMap()).remove(fixture.targetNode());
        verify(fixture.userManager(), never()).saveUser(any());
        verify(fixture.messagingService(), never()).pushUserUpdate(any());
    }

    @Test
    void refusesMutationWithoutCrossServerMessaging() {
        var fixture = fixture("default", false, false);

        var result = fixture.service().apply(command())
                .toCompletableFuture()
                .join();

        assertEquals("Failed", result.outcome());
        assertEquals("messaging_service_unavailable", result.failureCode());
        verify(fixture.userManager(), never()).saveUser(any());
    }

    @Test
    void repairsStoredGroupWhenCalculatedGroupDiffersFromExpectedAndTarget() {
        var fixture = fixture("admin", false, true);

        var result = fixture.service().apply(command())
                .toCompletableFuture()
                .join();

        assertEquals("Applied", result.outcome());
        assertEquals("vip", result.observedPrimaryGroup());
        verify(fixture.user()).setPrimaryGroup("vip");
        verify(fixture.userManager()).saveUser(fixture.user());
        verify(fixture.messagingService()).pushUserUpdate(fixture.user());
    }

    @Test
    void failsClosedWhenTargetNodeCannotBeAdded() {
        var fixture = fixture("default", false, true);
        when(fixture.nodeMap().add(fixture.targetNode()))
                .thenReturn(DataMutateResult.FAIL);

        var result = fixture.service().apply(command())
                .toCompletableFuture()
                .join();

        assertEquals("Failed", result.outcome());
        assertEquals("target_group_node_update_failed", result.failureCode());
        verify(fixture.user(), never()).setPrimaryGroup(any());
        verify(fixture.userManager(), never()).saveUser(any());
        verifyNoInteractions(fixture.messagingService());
    }

    @Test
    void restoresInMemoryNodesWhenManagedNodeCleanupFails() {
        var fixture = fixture("default", false, true);
        when(fixture.nodeMap().remove(fixture.defaultNode()))
                .thenReturn(DataMutateResult.FAIL);

        var result = fixture.service().apply(command())
                .toCompletableFuture()
                .join();

        assertEquals("Failed", result.outcome());
        assertEquals(
                "managed_group_node_cleanup_failed",
                result.failureCode());
        verify(fixture.nodeMap()).remove(fixture.targetNode());
        verify(fixture.user(), never()).setPrimaryGroup(any());
        verify(fixture.userManager(), never()).saveUser(any());
        verifyNoInteractions(fixture.messagingService());
    }

    @Test
    void treatsAlreadyPersistedTargetAsIdempotentSuccess() {
        var fixture = fixture("vip", true, true);
        when(fixture.nodeMap().toCollection())
                .thenReturn(List.of(fixture.targetNode()));
        when(fixture.user().setPrimaryGroup("vip"))
                .thenReturn(DataMutateResult.FAIL_ALREADY_HAS);

        var result = fixture.service().apply(command())
                .toCompletableFuture()
                .join();

        assertEquals("Applied", result.outcome());
        assertEquals("vip", result.observedPrimaryGroup());
        verify(fixture.userManager()).saveUser(fixture.user());
        verify(fixture.messagingService()).pushUserUpdate(fixture.user());
    }

    @Test
    void reportsFailureWhenBroadcastThrowsAfterSuccessfulSave() {
        var fixture = fixture("default", false, true);
        org.mockito.Mockito.doThrow(new IllegalStateException("simulated"))
                .when(fixture.messagingService())
                .pushUserUpdate(fixture.user());

        var result = fixture.service().apply(command())
                .toCompletableFuture()
                .join();

        assertEquals("Failed", result.outcome());
        assertEquals("luckperms_operation_failed", result.failureCode());
        verify(fixture.userManager()).saveUser(fixture.user());
        verify(fixture.userManager(), org.mockito.Mockito.times(2))
                .loadUser(command().minecraftUuid());
    }

    @Test
    void doesNotBroadcastWhenSaveFails() {
        var fixture = fixture("default", false, true);
        when(fixture.userManager().saveUser(fixture.user()))
                .thenReturn(CompletableFuture.failedFuture(
                        new IllegalStateException("simulated")));

        var result = fixture.service().apply(command())
                .toCompletableFuture()
                .join();

        assertEquals("Failed", result.outcome());
        assertEquals("luckperms_operation_failed", result.failureCode());
        verifyNoInteractions(fixture.messagingService());
        verify(fixture.userManager(), org.mockito.Mockito.times(2))
                .loadUser(command().minecraftUuid());
    }

    private static Fixture fixture(
            String calculatedGroup,
            boolean targetNodeAlreadyPresent,
            boolean messagingAvailable) {
        var luckPerms = mock(LuckPerms.class);
        var groupManager = mock(GroupManager.class);
        var userManager = mock(UserManager.class);
        var user = mock(User.class);
        var nodeMap = mock(NodeMap.class);
        var messagingService = mock(MessagingService.class);
        var defaultNode = inheritanceNode("default");
        var targetNode = inheritanceNode("vip");
        var minecraftUuid = command().minecraftUuid();

        when(luckPerms.getGroupManager()).thenReturn(groupManager);
        when(groupManager.getGroup("vip")).thenReturn(mock(Group.class));
        when(luckPerms.getUserManager()).thenReturn(userManager);
        when(userManager.loadUser(minecraftUuid))
                .thenReturn(CompletableFuture.completedFuture(user));
        when(userManager.saveUser(user))
                .thenReturn(CompletableFuture.completedFuture(null));
        when(user.getPrimaryGroup()).thenReturn(calculatedGroup);
        when(user.setPrimaryGroup("vip"))
                .thenReturn(DataMutateResult.SUCCESS);
        when(user.data()).thenReturn(nodeMap);
        when(nodeMap.toCollection()).thenReturn(targetNodeAlreadyPresent
                ? List.of(defaultNode, targetNode)
                : List.of(defaultNode));
        when(nodeMap.add(targetNode)).thenReturn(targetNodeAlreadyPresent
                ? DataMutateResult.FAIL_ALREADY_HAS
                : DataMutateResult.SUCCESS);
        when(nodeMap.remove(defaultNode)).thenReturn(DataMutateResult.SUCCESS);
        when(nodeMap.add(defaultNode)).thenReturn(DataMutateResult.SUCCESS);
        when(nodeMap.remove(targetNode)).thenReturn(DataMutateResult.SUCCESS);

        return new Fixture(
                new LuckPermsTierMutationService(
                        luckPerms,
                        ignored -> targetNode,
                        messagingAvailable
                                ? Optional.of(messagingService)
                                : Optional.empty()),
                userManager,
                user,
                nodeMap,
                messagingService,
                defaultNode,
                targetNode);
    }

    private static InheritanceNode inheritanceNode(String groupName) {
        var node = mock(InheritanceNode.class);
        var contexts = mock(
                net.luckperms.api.context.ImmutableContextSet.class);
        when(node.getGroupName()).thenReturn(groupName);
        when(contexts.isEmpty()).thenReturn(true);
        when(node.getContexts()).thenReturn(contexts);
        return node;
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
            MessagingService messagingService,
            InheritanceNode defaultNode,
            InheritanceNode targetNode) {
    }
}
