namespace TrafficAiPlugin.Splines;

public class SlowestAiStates
{
    private readonly AiState?[] _aiStates;
    private readonly ReaderWriterLockSlim _lock = new();

    public SlowestAiStates(int numPoints)
    {
        _aiStates = new AiState?[numPoints];
    }

    public AiState? this[int index]
    {
        get
        {
            var state = _aiStates[index];
            if (state == null) return null;

            // Valid: car is at this point
            if (state.CurrentSplinePointId == index) return state;

            // Valid: car is lane-changing (intentionally registered in target lane)
            if (state.IsCurrentlyLaneChanging) return state;

            // Stale: car moved forward but old registration wasn't cleaned
            return null;
        }
    }

    public void Enter(int pointId, AiState state)
    {
        _lock.EnterUpgradeableReadLock();
        try
        {
            ref var currentAiState = ref _aiStates[pointId];
            
            if (currentAiState != null && currentAiState.CurrentSplinePointId != pointId)
            {
                _lock.EnterWriteLock();
                try
                {
                    currentAiState = null;
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
            
            if (currentAiState == null || state.CurrentSpeed < currentAiState.CurrentSpeed)
            {
                _lock.EnterWriteLock();
                try
                {
                    currentAiState = state;
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
        }
        finally
        {
            _lock.ExitUpgradeableReadLock();
        }
    }
    
    public void Leave(int pointId, AiState state)
    {
        if (pointId < 0) return;
        
        _lock.EnterUpgradeableReadLock();
        try
        {
            if (_aiStates[pointId] == state)
            {
                _lock.EnterWriteLock();
                try
                {
                    _aiStates[pointId] = null;
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
        }
        finally
        {
            _lock.ExitUpgradeableReadLock();
        }
    }
}
