package world.hechao.tieragent;

record TierMutationResult(
        String outcome,
        String observedPrimaryGroup,
        String failureCode) {
    static TierMutationResult applied(String observedPrimaryGroup) {
        return new TierMutationResult("Applied", observedPrimaryGroup, null);
    }

    static TierMutationResult conflict(String observedPrimaryGroup) {
        return new TierMutationResult("Conflict", observedPrimaryGroup, null);
    }

    static TierMutationResult failed(
            String observedPrimaryGroup,
            String failureCode) {
        return new TierMutationResult(
                "Failed",
                observedPrimaryGroup,
                failureCode);
    }
}
