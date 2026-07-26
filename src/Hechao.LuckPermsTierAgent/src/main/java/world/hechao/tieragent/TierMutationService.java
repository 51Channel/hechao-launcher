package world.hechao.tieragent;

import java.util.concurrent.CompletionStage;

interface TierMutationService {
    CompletionStage<TierMutationResult> apply(TierCommand command);
}
