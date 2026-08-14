package world.hechao.tieragent;

import java.util.ArrayList;
import java.util.Locale;
import java.util.Optional;
import java.util.Set;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CompletionStage;
import java.util.function.Function;
import net.luckperms.api.LuckPerms;
import net.luckperms.api.messaging.MessagingService;
import net.luckperms.api.model.data.DataMutateResult;
import net.luckperms.api.model.user.User;
import net.luckperms.api.node.types.InheritanceNode;

final class LuckPermsTierMutationService implements TierMutationService {
    private static final Set<String> MANAGED_GROUPS =
            Set.of("default", "vip", "admin", "owner");

    private final LuckPerms luckPerms;
    private final Function<String, InheritanceNode> inheritanceNodeFactory;
    private final Optional<MessagingService> messagingService;

    LuckPermsTierMutationService(LuckPerms luckPerms) {
        this(
                luckPerms,
                group -> InheritanceNode.builder(group).build(),
                luckPerms.getMessagingService());
    }

    LuckPermsTierMutationService(
            LuckPerms luckPerms,
            Function<String, InheritanceNode> inheritanceNodeFactory,
            Optional<MessagingService> messagingService) {
        this.luckPerms = luckPerms;
        this.inheritanceNodeFactory = inheritanceNodeFactory;
        this.messagingService = messagingService;
    }

    @Override
    public CompletionStage<TierMutationResult> apply(TierCommand command) {
        String expected = normalize(command.expectedPrimaryGroup());
        String target = normalize(command.targetPrimaryGroup());
        if (!MANAGED_GROUPS.contains(target)) {
            return CompletableFuture.completedFuture(
                    TierMutationResult.failed(expected, "target_group_not_allowed"));
        }
        if (luckPerms.getGroupManager().getGroup(target) == null) {
            return CompletableFuture.completedFuture(
                    TierMutationResult.failed(expected, "target_group_missing"));
        }
        if (messagingService.isEmpty()) {
            return CompletableFuture.completedFuture(
                    TierMutationResult.failed(
                            expected,
                            "messaging_service_unavailable"));
        }

        return luckPerms.getUserManager()
                .loadUser(command.minecraftUuid())
                .thenCompose(user -> mutate(
                        command.minecraftUuid(),
                        user,
                        target))
                .exceptionally(ignored ->
                        TierMutationResult.failed(expected, "luckperms_operation_failed"));
    }

    private CompletionStage<TierMutationResult> mutate(
            java.util.UUID minecraftUuid,
            User user,
            String target) {
        String current = normalize(user.getPrimaryGroup());

        var managedGlobalNodes = managedGlobalNodes(user);
        var targetNode = managedGlobalNodes.stream()
                .filter(node -> target.equals(normalize(node.getGroupName())))
                .findFirst()
                .orElseGet(() -> inheritanceNodeFactory.apply(target));
        var targetNodeResult = user.data().add(targetNode);
        if (targetNodeResult != DataMutateResult.SUCCESS &&
                targetNodeResult != DataMutateResult.FAIL_ALREADY_HAS) {
            return CompletableFuture.completedFuture(
                    TierMutationResult.failed(
                            current,
                            "target_group_node_update_failed"));
        }

        var removedNodes = new ArrayList<InheritanceNode>();
        for (var node : managedGlobalNodes) {
            if (target.equals(normalize(node.getGroupName()))) {
                continue;
            }

            var removeResult = user.data().remove(node);
            if (removeResult == DataMutateResult.SUCCESS) {
                removedNodes.add(node);
                continue;
            }
            if (removeResult != DataMutateResult.FAIL_LACKS) {
                restoreNodes(user, targetNode, targetNodeResult, removedNodes);
                return CompletableFuture.completedFuture(
                        TierMutationResult.failed(
                                current,
                                "managed_group_node_cleanup_failed"));
            }
        }

        var primaryGroupResult = user.setPrimaryGroup(target);
        if (primaryGroupResult != DataMutateResult.SUCCESS &&
                primaryGroupResult != DataMutateResult.FAIL_ALREADY_HAS) {
            restoreNodes(user, targetNode, targetNodeResult, removedNodes);
            return CompletableFuture.completedFuture(
                    TierMutationResult.failed(
                            current,
                            "primary_group_update_failed"));
        }

        return luckPerms.getUserManager()
                .saveUser(user)
                .thenApply(ignored -> {
                    messagingService.orElseThrow().pushUserUpdate(user);
                    return TierMutationResult.applied(target);
                })
                .exceptionallyCompose(ignored ->
                        reloadAfterFailure(minecraftUuid, current));
    }

    private CompletionStage<TierMutationResult> reloadAfterFailure(
            java.util.UUID minecraftUuid,
            String observedPrimaryGroup) {
        return luckPerms.getUserManager()
                .loadUser(minecraftUuid)
                .handle((ignored, reloadFailure) -> TierMutationResult.failed(
                        observedPrimaryGroup,
                        "luckperms_operation_failed"));
    }

    private static void restoreNodes(
            User user,
            InheritanceNode targetNode,
            DataMutateResult targetNodeResult,
            java.util.List<InheritanceNode> removedNodes) {
        removedNodes.forEach(user.data()::add);
        if (targetNodeResult == DataMutateResult.SUCCESS) {
            user.data().remove(targetNode);
        }
    }

    private static java.util.List<InheritanceNode> managedGlobalNodes(User user) {
        return user.data().toCollection().stream()
                .filter(InheritanceNode.class::isInstance)
                .map(InheritanceNode.class::cast)
                .filter(node -> node.getContexts().isEmpty())
                .filter(node -> MANAGED_GROUPS.contains(
                        normalize(node.getGroupName())))
                .toList();
    }

    private static String normalize(String value) {
        return value == null ? "default" : value.trim().toLowerCase(Locale.ROOT);
    }
}
