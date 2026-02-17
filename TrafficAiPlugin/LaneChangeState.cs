using System.Numerics;
using JPBotelho;

namespace TrafficAiPlugin;

public class LaneChangeState
{
    public bool IsChangingLane { get; private set; }
    public bool IsAborting { get; private set; }
    public float Progress { get; private set; }
    public int SourcePointId { get; private set; } = -1;
    public int TargetPointId { get; private set; } = -1;
    public bool IsOvertake { get; private set; }
    public float StartY => _startPosition.Y;
    public float EndY => _endPosition.Y;
    public float TotalDistance => _totalDistance;

    private Vector3 _startPosition;
    private Vector3 _endPosition;
    private Vector3 _startTangent;
    private Vector3 _endTangent;
    private float _totalDistance;
    private float _distanceTravelled;
    private float _startCamber;
    private float _endCamber;

    public void Begin(
        int sourcePointId,
        int targetPointId,
        Vector3 startPosition,
        Vector3 endPosition,
        Vector3 startTangent,
        Vector3 endTangent,
        float startCamber,
        float endCamber,
        bool isOvertake)
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
    }

    public void BeginAbortReturn(
        Vector3 currentPosition,
        Vector3 currentTangent,
        Vector3 returnPosition,
        Vector3 returnTangent,
        int returnPointId,
        float returnCamber)
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
