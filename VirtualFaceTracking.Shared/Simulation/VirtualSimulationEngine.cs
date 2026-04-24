using VirtualFaceTracking.Shared.Abstractions;

namespace VirtualFaceTracking.Shared.Simulation;

public sealed class VirtualSimulationEngine : IVirtualSimulationEngine
{
    private readonly Random _random;

    private double _elapsedSeconds;
    private double _nextFixationAt;
    private double _nextBlinkAt;
    private bool _doubleBlinkPending;
    private double _blinkProgress = -1d;
    private double _blinkDurationSeconds = 0.18d;

    private float _leftEyeYawTarget;
    private float _rightEyeYawTarget;
    private float _leftEyePitchTarget;
    private float _rightEyePitchTarget;
    private float _leftEyeYawCurrent;
    private float _rightEyeYawCurrent;
    private float _leftEyePitchCurrent;
    private float _rightEyePitchCurrent;

    private float _browPhase;
    private float _facePhase;
    private float _microPhase;

    public VirtualSimulationEngine(int? seed = null)
    {
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
        ScheduleFixation(0f);
        ScheduleBlink(0d, 1f);
    }

    public SimulationOffsets Compute(TrackerRuntimeState state, TimeSpan elapsed)
    {
        var offsets = new SimulationOffsets();
        var dt = Math.Clamp((float)elapsed.TotalSeconds, 0.001f, 0.1f);
        _elapsedSeconds += dt;

        state.Simulation.Clamp();

        if (!state.Simulation.Enabled || state.Simulation.Intensity <= 0f)
        {
            return offsets;
        }

        var intensity = state.Simulation.Intensity;
        var speed = 0.25f + (state.Simulation.Speed * 2.75f);

        _microPhase += dt * speed * 23f;
        _browPhase += dt * speed * 0.95f;
        _facePhase += dt * speed * 0.72f;

        if (state.Simulation.SimulateEyes)
        {
            UpdateEyes(offsets, dt, intensity, speed);
        }

        if (state.Simulation.SimulateBlink)
        {
            UpdateBlink(offsets, dt, intensity, speed);
        }

        if (state.Simulation.SimulateBrows)
        {
            UpdateBrows(offsets, intensity);
        }

        if (state.Simulation.SimulateFace)
        {
            UpdateFace(offsets, intensity);
        }

        offsets.Clamp();
        return offsets;
    }

    private void UpdateEyes(SimulationOffsets offsets, float dt, float intensity, float speed)
    {
        if (_elapsedSeconds >= _nextFixationAt)
        {
            ScheduleFixation(speed);
        }

        var eyeLerp = 1f - MathF.Exp(-(2.5f + speed * 3f) * dt);
        _leftEyeYawCurrent = Damp(_leftEyeYawCurrent, _leftEyeYawTarget, eyeLerp);
        _rightEyeYawCurrent = Damp(_rightEyeYawCurrent, _rightEyeYawTarget, eyeLerp);
        _leftEyePitchCurrent = Damp(_leftEyePitchCurrent, _leftEyePitchTarget, eyeLerp);
        _rightEyePitchCurrent = Damp(_rightEyePitchCurrent, _rightEyePitchTarget, eyeLerp);

        var micro = MathF.Sin(_microPhase) * 0.018f * intensity;
        var microOffset = MathF.Cos(_microPhase * 1.21f) * 0.012f * intensity;

        offsets.LeftEyeYaw = _leftEyeYawCurrent + micro;
        offsets.RightEyeYaw = _rightEyeYawCurrent - micro * 0.8f;
        offsets.LeftEyePitch = _leftEyePitchCurrent + microOffset;
        offsets.RightEyePitch = _rightEyePitchCurrent - microOffset * 0.85f;
    }

    private void UpdateBlink(SimulationOffsets offsets, float dt, float intensity, float speed)
    {
        if (_blinkProgress < 0d && _elapsedSeconds >= _nextBlinkAt)
        {
            _blinkProgress = 0d;
            _blinkDurationSeconds = 0.11d + (_random.NextDouble() * 0.11d);
        }

        if (_blinkProgress >= 0d)
        {
            _blinkProgress += dt / _blinkDurationSeconds;
            var normalized = (float)Math.Clamp(_blinkProgress, 0d, 1d);
            var envelope = MathF.Sin(normalized * MathF.PI);
            var amount = envelope * (0.55f + intensity * 0.35f);
            offsets.LeftEyeBlink = amount;
            offsets.RightEyeBlink = amount;

            if (_blinkProgress >= 1d)
            {
                _blinkProgress = -1d;
                if (_doubleBlinkPending)
                {
                    _doubleBlinkPending = false;
                    _nextBlinkAt = _elapsedSeconds + 0.1d;
                }
                else
                {
                    _doubleBlinkPending = _random.NextDouble() < 0.15d;
                    ScheduleBlink(_elapsedSeconds, speed);
                }
            }
        }
    }

    private void UpdateBrows(SimulationOffsets offsets, float intensity)
    {
        var leftWave = 0.5f + 0.5f * MathF.Sin(_browPhase);
        var rightWave = 0.5f + 0.5f * MathF.Sin(_browPhase + 0.9f);
        var lowerWave = 0.5f + 0.5f * MathF.Sin((_browPhase * 1.24f) + 2.1f);

        offsets.LeftBrowRaise = leftWave * 0.11f * intensity;
        offsets.RightBrowRaise = rightWave * 0.11f * intensity;
        offsets.LeftBrowLower = lowerWave * 0.08f * intensity;
        offsets.RightBrowLower = (0.5f + 0.5f * MathF.Sin((_browPhase * 1.24f) + 2.8f)) * 0.08f * intensity;
    }

    private void UpdateFace(SimulationOffsets offsets, float intensity)
    {
        var faceSigned = MathF.Sin(_facePhase) * 0.15f * intensity;
        var faceWaveA = 0.5f + 0.5f * MathF.Sin((_facePhase * 0.8f) + 0.7f);
        var faceWaveB = 0.5f + 0.5f * MathF.Sin((_facePhase * 1.1f) + 1.8f);
        var faceWaveC = 0.5f + 0.5f * MathF.Sin((_facePhase * 1.35f) + 2.6f);

        offsets.JawOpen = faceWaveA * 0.06f * intensity;
        offsets.JawSideways = faceSigned;
        offsets.JawForwardBack = MathF.Sin((_facePhase * 0.65f) + 1.4f) * 0.12f * intensity;
        offsets.MouthOpen = faceWaveB * 0.08f * intensity;
        offsets.Smile = faceWaveC * 0.06f * intensity;
        offsets.Frown = (0.5f + 0.5f * MathF.Sin((_facePhase * 0.9f) + 4.2f)) * 0.05f * intensity;
        offsets.LipPucker = (0.5f + 0.5f * MathF.Sin((_facePhase * 0.85f) + 5.2f)) * 0.05f * intensity;
        offsets.LipFunnel = (0.5f + 0.5f * MathF.Sin((_facePhase * 0.92f) + 2.2f)) * 0.05f * intensity;
        offsets.LipSuck = (0.5f + 0.5f * MathF.Sin((_facePhase * 1.05f) + 3.1f)) * 0.04f * intensity;
        offsets.CheekPuffSuck = MathF.Sin((_facePhase * 0.77f) + 0.35f) * 0.18f * intensity;
        offsets.CheekSquint = (0.5f + 0.5f * MathF.Sin((_facePhase * 1.16f) + 1.1f)) * 0.06f * intensity;
        offsets.NoseSneer = (0.5f + 0.5f * MathF.Sin((_facePhase * 1.28f) + 0.4f)) * 0.05f * intensity;
    }

    private void ScheduleFixation(float speed)
    {
        var baseYaw = (float)(_random.NextDouble() * 0.7d - 0.35d);
        var basePitch = (float)(_random.NextDouble() * 0.44d - 0.22d);
        var asymmetryYaw = (float)(_random.NextDouble() * 0.08d - 0.04d);
        var asymmetryPitch = (float)(_random.NextDouble() * 0.06d - 0.03d);

        _leftEyeYawTarget = baseYaw + asymmetryYaw;
        _rightEyeYawTarget = baseYaw - asymmetryYaw;
        _leftEyePitchTarget = basePitch + asymmetryPitch;
        _rightEyePitchTarget = basePitch - asymmetryPitch;
        _nextFixationAt = _elapsedSeconds + (0.5d + (_random.NextDouble() * 1.5d)) / Math.Max(speed, 0.25f);
    }

    private void ScheduleBlink(double from, float speed)
    {
        _nextBlinkAt = from + (2.1d + (_random.NextDouble() * 3.2d)) / Math.Max(speed, 0.25f);
    }

    private static float Damp(float current, float target, float amount) => current + ((target - current) * amount);
}
