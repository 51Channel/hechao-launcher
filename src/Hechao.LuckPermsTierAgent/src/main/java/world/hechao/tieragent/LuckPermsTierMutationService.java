package world.hechao.tieragent;

import java.util.Locale;
import java.util.Set;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CompletionStage;
import java.util.function.Function;
import net.luckperms.api.LuckPerms;
import net.luckperms.api.model.user.User;
import net.luckperms.api.node.types.InheritanceNode;

final class LuckPermsTierMutationService implements TierMutationService {
    private static final Set<String> MANAGED_GROUPS =
            Set.of("default", "vip", "admin", "owner");

    private final LuckPerms luckPerms;
    private final Function<String, InheritanceNode> inheritanceNodeFactory;

    LuckPermsTierMutationService(LuckPerms luckPerms) {
        this(
                luckPerms,
                group -> InheritanceNode.builder(group).build());
    }

    LuckPermsTierMutationService(
            LuckPerms luckPerms,
            Function<String, InheritanceNode> inheritanceNodeFactory) {
        this.luckPerms = luckPerms;
        this.inheritanceNodeFactory = inheritanceNodeFactory;
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

        return luckPerms.getUserManager()
                .loadUser(command.minecraftUuid())
                .thenCompose(user -> mutate(user, expected, target))
                .exceptionally(ignored ->
                        TierMutationResult.failed(expected, "luckperms_operation_failed"));
    }

    private CompletionStage<TierMutationResult> mutate(
            User user,
            String expected,
            String target) {
        String current = normalize(user.getPrimaryGroup());
        if (target.equals(current)) {
            return CompletableFuture.completedFuture(
                    TierMutationResult.applied(current));
        }
        if (!expected.equals(current)) {
            return CompletableFuture.completedFuture(
                    TierMutationResult.conflict(current));
        }

        var primaryGroupResult = user.setPrimaryGroup(target);
        if (!primaryGroupResult.wasSuccessful()) {
            return CompletableFuture.completedFuture(
                    TierMutationResult.failed(
                            current,
                            "primary_group_update_failed"));
        }

        var managedGlobalNodes = user.data().toCollection().stream()
                .filter(InheritanceNode.class::isInstance)
                .map(InheritanceNode.class::cast)
                .filter(node -> node.getContexts().isEmpty())
                .filter(node -> MANAGED_GROUPS.contains(
                        normalize(node.getGroupName())))
                .toList();
        managedGlobalNodes.forEach(user.data()::remove);
        user.data().add(inheritanceNodeFactory.apply(target));

        return luckPerms.getUserManager()
                .saveUser(user)
                .thenApply(ignored -> {
                    String observed = normalize(user.getPrimaryGroup());
                    return target.equals(observed)
                            ? TierMutationResult.applied(observed)
                            : TierMutationResult.failed(
                                    observed,
                                    "primary_group_not_changed");
                });
    }

    private static String normalize(String value) {
        return value == null ? "default" : value.trim().toLowerCase(Locale.ROOT);
    }
}
