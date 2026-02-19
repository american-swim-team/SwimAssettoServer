using System.Numerics;
using JPBotelho;
using TrafficAiPlugin.Shared.Splines;

namespace TrafficAiPlugin;

public class LaneChangeState
{
    public bool IsChangingLane { get; private set; }
    public bool IsAborting { get; private set; }
    public float Progress { get; private set; }
    public int SourcePointId { get; private set; } = -1;
    public int TargetPointId { get; private set; } = -1;
    public bool IsOvertake { get; private set; }
    public float TotalDistance => _totalDistance;

    private Vector3 _startPosition;
    private Vector3 _endPosition;
    private Vector3 _startTangent;
    private Vector3 _endTangent;
    private float _totalDistance;
    private float _distanceTravelled;
    private float _startCamber;
    private float _endCamber;

    // Height/camber profile arrays for terrain-following during lane changes
    private const int MaxProfilePoints = 64;

    private readonly float[] _sourceProfileDist = new float[MaxProfilePoints];
    private readonly float[] _sourceProfileY = new float[MaxProfilePoints];
    private readonly float[] _sourceProfileCamber = new float[MaxProfilePoints];
    private int _sourceProfileCount;

    private readonly float[] _targetProfileDist = new float[MaxProfilePoints];
    private readonly float[] _targetProfileY = new float[MaxProfilePoints];
    private readonly float[] _targetProfileCamber = new float[MaxProfilePoints];
    private int _targetProfileCount;

    public void Begin(
        int sourcePointId,
        int targetPointId,
        int targetStartPointId,
        Vector3 startPosition,
        Vector3 endPosition,
        Vector3 startTangent,
        Vector3 endTangent,
        float startCamber,
        float endCamber,
        bool isOvertake,
        ReadOnlySpan<SplinePoint> points)
    {
        IsChangingLane = true;
        Progress = 0;
        SourcePointId = sourcePointId;
        TargetPointId = targetPointId;
        _startPosition = startPosition;
        _endPosition = endPosition;
        _startTangent = startTangent;
        _endTangent = endTangent;
        _startCamber = startCamber;
        _endCamber = endCamber;
        IsOvertake = isOvertake;
        _distanceTravelled = 0;

        _totalDistance = EstimateCurveLength(startPosition, endPosition, startTangent, endTangent);

        // Build height+camber profiles from both lanes' spline points
        float profileDistance = _totalDistance * 1.1f;
        _sourceProfileCount = BuildProfile(points, sourcePointId, profileDistance,
            _sourceProfileDist, _sourceProfileY, _sourceProfileCamber);
        _targetProfileCount = BuildProfile(points, targetStartPointId, profileDistance,
            _targetProfileDist, _targetProfileY, _targetProfileCamber);
    }

    public void BeginAbortReturn(
        Vector3 currentPosition,
        Vector3 currentTangent,
        Vector3 returnPosition,
        Vector3 returnTangent,
        int returnPointId,
        float returnCamber,
        int sourceWalkStartId,
        ReadOnlySpan<SplinePoint> points)
    {
        // Capture current camber before resetting progress
        float currentCamber = GetInterpolatedCamber();

        IsAborting = true;
        Progress = 0;
        _distanceTravelled = 0;

        _startPosition = currentPosition;
        _endPosition = returnPosition;
        // Use current tangent direction for smooth continuation, scale by distance
        float returnDistance = Vector3.Distance(currentPosition, returnPosition);
        _startTangent = Vector3.Normalize(currentTangent) * returnDistance * 0.5f;
        _endTangent = Vector3.Normalize(returnTangent) * returnDistance * 0.5f;

        _startCamber = currentCamber;
        _endCamber = returnCamber;

        // SourcePointId stays as-is (the original source lane we're returning to)
        TargetPointId = returnPointId;

        _totalDistance = EstimateCurveLength(_startPosition, _endPosition, _startTangent, _endTangent);

        // Build source lane profile for abort return height tracking
        _sourceProfileCount = BuildProfile(points, sourceWalkStartId,
            _totalDistance * 1.1f, _sourceProfileDist, _sourceProfileY, _sourceProfileCamber);
        _targetProfileCount = 0;
    }

    public void UpdateProgress(float distanceMoved)
    {
        if (!IsChangingLane || _totalDistance <= 0)
            return;

        _distanceTravelled += distanceMoved;
        Progress = Math.Clamp(_distanceTravelled / _totalDistance, 0, 1);
    }

    public CatmullRom.CatmullRomPoint GetInterpolatedPoint()
    {
        return CatmullRom.Evaluate(_startPosition, _endPosition, _startTangent, _endTangent, Progress);
    }

    public Vector3 GetInterpolatedPosition()
    {
        return CatmullRom.CalculatePosition(_startPosition, _endPosition, _startTangent, _endTangent, Progress);
    }

    public Vector3 GetInterpolatedTangent()
    {
        return CatmullRom.CalculateTangent(_startPosition, _endPosition, _startTangent, _endTangent, Progress);
    }

    public float GetInterpolatedCamber()
    {
        return _startCamber + (_endCamber - _startCamber) * Progress;
    }

    public bool IsComplete => Progress >= 1.0f;

    public void Complete()
    {
        IsChangingLane = false;
        IsAborting = false;
        Progress = 0;
        SourcePointId = -1;
        TargetPointId = -1;
        _distanceTravelled = 0;
    }

    public void Abort()
    {
        IsChangingLane = false;
        IsAborting = false;
        Progress = 0;
        SourcePointId = -1;
        TargetPointId = -1;
        _distanceTravelled = 0;
    }

    public float GetBlendedHeight()
    {
        if (!IsChangingLane) return 0;

        if (IsAborting)
        {
            float totalDist = _sourceProfileCount > 0
                ? _sourceProfileDist[_sourceProfileCount - 1] : 0;
            float mappedDist = Progress * totalDist;
            float sourceY = LookupHeight(
                _sourceProfileDist, _sourceProfileY, _sourceProfileCount, mappedDist);
            return _startPosition.Y * (1 - Progress) + sourceY * Progress;
        }

        float srcY = LookupHeight(
            _sourceProfileDist, _sourceProfileY, _sourceProfileCount, _distanceTravelled);
        float tgtY = LookupHeight(
            _targetProfileDist, _targetProfileY, _targetProfileCount, _distanceTravelled);
        return srcY * (1 - Progress) + tgtY * Progress;
    }

    public float GetBlendedPitchSlope()
    {
        if (!IsChangingLane) return 0;

        if (IsAborting)
        {
            float totalDist = _sourceProfileCount > 0
                ? _sourceProfileDist[_sourceProfileCount - 1] : 0;
            float mappedDist = Progress * totalDist;
            return LookupSlope(
                _sourceProfileDist, _sourceProfileY, _sourceProfileCount, mappedDist);
        }

        float srcSlope = LookupSlope(
            _sourceProfileDist, _sourceProfileY, _sourceProfileCount, _distanceTravelled);
        float tgtSlope = LookupSlope(
            _targetProfileDist, _targetProfileY, _targetProfileCount, _distanceTravelled);
        return srcSlope * (1 - Progress) + tgtSlope * Progress;
    }

    public float GetBlendedCamber()
    {
        if (!IsChangingLane) return 0;

        if (IsAborting)
        {
            float totalDist = _sourceProfileCount > 0
                ? _sourceProfileDist[_sourceProfileCount - 1] : 0;
            float mappedDist = Progress * totalDist;
            float sourceCamber = LookupHeight(
                _sourceProfileDist, _sourceProfileCamber, _sourceProfileCount, mappedDist);
            return _startCamber * (1 - Progress) + sourceCamber * Progress;
        }

        float srcCamber = LookupHeight(
            _sourceProfileDist, _sourceProfileCamber, _sourceProfileCount, _distanceTravelled);
        float tgtCamber = LookupHeight(
            _targetProfileDist, _targetProfileCamber, _targetProfileCount, _distanceTravelled);
        return srcCamber * (1 - Progress) + tgtCamber * Progress;
    }

    private static int BuildProfile(
        ReadOnlySpan<SplinePoint> points,
        int startPointId,
        float maxDistance,
        float[] outDist,
        float[] outY,
        float[] outCamber)
    {
        if (startPointId < 0 || startPointId >= points.Length)
            return 0;

        int count = 0;
        float cumDist = 0;
        int currentId = startPointId;

        outDist[0] = 0;
        outY[0] = points[currentId].Position.Y;
        outCamber[0] = points[currentId].Camber;
        count = 1;

        while (cumDist < maxDistance && currentId >= 0 && count < MaxProfilePoints)
        {
            float segLen = points[currentId].Length;
            int nextId = points[currentId].NextId;
            if (nextId < 0) break;

            cumDist += segLen;
            outDist[count] = cumDist;
            outY[count] = points[nextId].Position.Y;
            outCamber[count] = points[nextId].Camber;
            count++;

            currentId = nextId;
        }

        return count;
    }

    private static float LookupHeight(float[] dist, float[] y, int count, float distance)
    {
        if (count == 0) return 0;
        if (count == 1 || distance <= dist[0]) return y[0];
        if (distance >= dist[count - 1]) return y[count - 1];

        for (int i = 1; i < count; i++)
        {
            if (distance <= dist[i])
            {
                float t = (distance - dist[i - 1]) / (dist[i] - dist[i - 1]);
                return y[i - 1] + (y[i] - y[i - 1]) * t;
            }
        }
        return y[count - 1];
    }

    private static float LookupSlope(float[] dist, float[] y, int count, float distance)
    {
        if (count < 2) return 0;

        int idx = (distance <= dist[0]) ? 1 : count - 1;
        for (int i = 1; i < count; i++)
        {
            if (distance <= dist[i]) { idx = i; break; }
        }
        float dd = dist[idx] - dist[idx - 1];
        return dd > 0 ? (y[idx] - y[idx - 1]) / dd : 0;
    }

    private static float EstimateCurveLength(Vector3 start, Vector3 end, Vector3 tanStart, Vector3 tanEnd, int samples = 10)
    {
        float length = 0;
        Vector3 previousPoint = start;

        for (int i = 1; i <= samples; i++)
        {
            float t = i / (float)samples;
            Vector3 currentPoint = CatmullRom.CalculatePosition(start, end, tanStart, tanEnd, t);
            length += Vector3.Distance(previousPoint, currentPoint);
            previousPoint = currentPoint;
        }

        return length;
    }
}
