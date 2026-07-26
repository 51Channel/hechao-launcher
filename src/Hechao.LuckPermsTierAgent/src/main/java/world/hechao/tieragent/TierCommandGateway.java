package world.hechao.tieragent;

import java.io.IOException;
import java.util.List;

interface TierCommandGateway {
    List<TierCommand> claim() throws IOException, InterruptedException;

    void complete(TierCommand command, TierMutationResult result)
            throws IOException, InterruptedException;
}
